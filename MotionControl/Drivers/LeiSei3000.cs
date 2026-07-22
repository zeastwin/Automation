using System;
// 模块：运动控制 / 驱动。
// 职责范围：封装雷赛运动控制卡 SDK 与具体硬件调用。

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using csLTDMC;

namespace Automation
{
    public class LS
    {
        private readonly CardConfigStore cardStore;

        public LS(CardConfigStore cardStore)
        {
            this.cardStore = cardStore ?? throw new ArgumentNullException(nameof(cardStore));
        }

        public bool IsCardInitialized { get; private set; }
        private readonly object profileLock = new object();
        private readonly object ioOutputLock = new object();
        private readonly Dictionary<long, MotionProfile> appliedProfiles = new Dictionary<long, MotionProfile>();

        private sealed class MotionProfile
        {
            public double MinVel;
            public double MaxVel;
            public double Acc;
            public double Dec;
            public double StopVel;
            public double SPara;
            public int Equiv;

            public bool Matches(double minVel, double maxVel, double acc, double dec, double stopVel, double sPara, int equiv)
            {
                return MinVel == minVel && MaxVel == maxVel && Acc == acc && Dec == dec
                    && StopVel == stopVel && SPara == sPara && Equiv == equiv;
            }
        }

        private static void EnsureSuccess(short result, string operation, ushort card, ushort axis)
        {
            if (result != 0)
            {
                throw new InvalidOperationException($"运动控制调用失败:{operation},卡{card},轴{axis},错误码{result}");
            }
        }
        public ushort InitCard()
        {
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
            IsCardInitialized = true;
            short num = LTDMC.dmc_board_init();//获取卡数量
            if (num <= 0 || num > 8)
            {
                IsCardInitialized = false;
            }
            ushort _num = 0;
            ushort[] cardids = new ushort[8];
            uint[] cardtypes = new uint[8];
            short res = LTDMC.dmc_get_CardInfList(ref _num, cardtypes, cardids);
            if (res != 0)
            {
                IsCardInitialized = false;
            }
            if (!IsCardInitialized)
            {
                return 0;
            }
            return cardids[0];
        }
        //设置运动参数
        public void SetMovParam(ushort card,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv)
        {
            if (equiv <= 0 || dMaxVel <= 0 || acc <= 0 || dec <= 0
                || minVel < 0 || dStopVel < 0 || dS_para < 0
                || double.IsNaN(minVel) || double.IsInfinity(minVel)
                || double.IsNaN(dMaxVel) || double.IsInfinity(dMaxVel)
                || double.IsNaN(acc) || double.IsInfinity(acc)
                || double.IsNaN(dec) || double.IsInfinity(dec)
                || double.IsNaN(dStopVel) || double.IsInfinity(dStopVel)
                || double.IsNaN(dS_para) || double.IsInfinity(dS_para))
            {
                throw new ArgumentOutOfRangeException(nameof(dMaxVel), $"运动参数无效:卡{card},轴{axis}");
            }
            long key = ((long)card << 32) | axis;
            lock (profileLock)
            {
                if (appliedProfiles.TryGetValue(key, out MotionProfile profile)
                    && profile.Matches(minVel, dMaxVel, acc, dec, dStopVel, dS_para, equiv))
                {
                    return;
                }
            }
             //axis; //轴号
             //dEquiv; //脉冲当量
             //dStartVel;//起始速度
             //dMaxVel;//运行速度
             //dTacc;//加速时间
             //dTdec;//减速时间
             //dStopVel;//停止速度
             //dS_para;//S段时间
             EnsureSuccess(LTDMC.dmc_set_equiv(card, axis, equiv), "设置脉冲当量", card, axis);

              EnsureSuccess(LTDMC.dmc_set_profile_unit(card, axis, minVel, dMaxVel, acc, dec, dStopVel), "设置速度参数", card, axis);
           // LTDMC.dmc_set_acc_profile(card, axis, minVel, dMaxVel* equiv, acc* equiv, dec* equiv, dStopVel);
          //  LTDMC.dmc_set_acc_profile(card, axis, minVel, dMaxVel, acc, dec, dStopVel);

             EnsureSuccess(LTDMC.dmc_set_s_profile(card, axis, 0, dS_para), "设置S曲线", card, axis);

             lock (profileLock)
             {
                 appliedProfiles[key] = new MotionProfile
                 {
                     MinVel = minVel,
                     MaxVel = dMaxVel,
                     Acc = acc,
                     Dec = dec,
                     StopVel = dStopVel,
                     SPara = dS_para,
                     Equiv = equiv
                 };
             }

          //  LTDMC.dmc_set_dec_stop_time(_CardID, axis, dTdec); //设置减速停止时间

           
        }
        //定长绝对运动或相对运动
        public void Mov(ushort card, ushort axis, double dDist, ushort sPosi_mode,bool wait)
        {
            if (double.IsNaN(dDist) || double.IsInfinity(dDist) || (sPosi_mode != 0 && sPosi_mode != 1))
            {
                throw new ArgumentOutOfRangeException(nameof(dDist), $"目标位置或运动模式无效:卡{card},轴{axis}");
            }
            //dDist;//目标位置
            //sPosi_mode; //运动模式0：相对坐标模式，1：绝对坐标模式
            EnsureSuccess(LTDMC.dmc_pmove_unit(card, axis, dDist, sPosi_mode), "定长运动", card, axis);

            while (!GetInPos(card, axis) && wait)
            {
                Thread.Sleep(1);
            }
        }

        public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
        {
            if (request == null || request.Axes == null || request.Positions == null
                || request.Axes.Count == 0 || request.Axes.Count != request.Positions.Count
                || request.Axes.Count > 6 || request.CoordinateSystem > 1
                || request.MaxVelocity <= 0 || request.AccelerationTime <= 0 || request.DecelerationTime <= 0
                || double.IsNaN(request.MaxVelocity) || double.IsInfinity(request.MaxVelocity)
                || double.IsNaN(request.AccelerationTime) || double.IsInfinity(request.AccelerationTime)
                || double.IsNaN(request.DecelerationTime) || double.IsInfinity(request.DecelerationTime)
                || (request.PositionMode != 0 && request.PositionMode != 1))
            {
                throw new ArgumentException("协调直线运动参数无效。", nameof(request));
            }
            ushort[] axes = request.Axes.ToArray();
            double[] positions = request.Positions.ToArray();
            if (axes.Distinct().Count() != axes.Length)
            {
                throw new ArgumentException("协调直线运动轴配置重复。", nameof(request));
            }
            for (int i = 0; i < positions.Length; i++)
            {
                if (double.IsNaN(positions[i]) || double.IsInfinity(positions[i]))
                {
                    throw new ArgumentException($"协调直线运动位置无效:轴{axes[i]}", nameof(request));
                }
            }
            EnsureSuccess(LTDMC.dmc_set_vector_profile_unit(request.Card, request.CoordinateSystem,
                0, request.MaxVelocity, request.AccelerationTime, request.DecelerationTime, 0),
                "设置协调直线运动参数", request.Card, request.CoordinateSystem);
            EnsureSuccess(LTDMC.dmc_set_vector_s_profile(request.Card, request.CoordinateSystem, 0, 0),
                "设置协调直线S曲线", request.Card, request.CoordinateSystem);
            EnsureSuccess(LTDMC.dmc_line_unit(request.Card, request.CoordinateSystem,
                (ushort)axes.Length, axes, positions, request.PositionMode),
                "启动协调直线运动", request.Card, request.CoordinateSystem);
        }

        public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
        {
            short result = LTDMC.dmc_check_done_multicoor(card, coordinateSystem);
            if (result == 0)
            {
                return false;
            }
            if (result == 1)
            {
                return true;
            }
            throw new InvalidOperationException($"读取协调直线运动状态失败:卡{card},坐标系{coordinateSystem},返回值{result}");
        }

        public void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode)
        {
            EnsureSuccess(LTDMC.dmc_stop_multicoor(card, coordinateSystem, stopMode),
                "停止协调直线运动", card, coordinateSystem);
        }
        //连续运动
        public void Jog(ushort card, ushort axis, ushort sDir)
        {
           // ushort sDir; //运动方向，0：负方向，1：正方向
             EnsureSuccess(LTDMC.dmc_vmove(card, axis, sDir), "连续运动", card, axis);
        }

        //制动
        public void StopOneAxis(ushort card, ushort axis, ushort stop_mode)
        {
            //stop_mode//制动方式，0：减速停止，1：紧急停止

             EnsureSuccess(LTDMC.dmc_stop(card, axis, stop_mode), "停止轴", card, axis);
        }

        // 读取指定轴运动状态
        public bool GetInPos(ushort card,ushort axis) //检测轴是否到位
        {
            short result = LTDMC.dmc_check_done(card, axis);
            if (result == 0)
            {
                return false;
            }
            if (result == 1)
            {
                return true;
            }
            throw new InvalidOperationException($"读取轴运动状态失败:卡{card},轴{axis},返回值{result}");
        }
        // 读取指定轴使能状态
        public bool GetAxisSevon(ushort card, ushort axis) 
        {
            short result = LTDMC.dmc_read_sevon_pin(card, axis);
            if (result == 0)
            {
                return true;
            }
            if (result == 1)
            {
                return false;
            }
            throw new InvalidOperationException($"读取轴使能状态失败:卡{card},轴{axis},返回值{result}");
        }
        // 设置指定轴使能状态
        public void SetAxisSevon(ushort card, ushort axis,bool isSevon)
        {
            ushort temp = isSevon ? (ushort)0 : (ushort)1;
             EnsureSuccess(LTDMC.dmc_write_sevon_pin(card,axis, temp), "设置轴使能", card, axis);
        }
        //读取指定轴的位置
        public double GetAxisPos(ushort card, ushort axis)
        {
            double pos = 0;
            LTDMC.dmc_get_position_unit(card, axis, ref pos);
            return pos;
        }
        //读取指定轴的编码器位置
        public double GetAxisPosEncoder(ushort card, ushort axis)
        {
            double pos = 0;
            EnsureSuccess(LTDMC.dmc_get_encoder_unit(card, axis, ref pos), "读取编码器位置", card, axis);
            return pos;
        }
        public void StopConnect()
        {
            short result = LTDMC.dmc_board_close();//控制卡关闭函数，释放系统资源
            if (result != 0)
            {
                throw new InvalidOperationException($"关闭运动控制卡失败:错误码{result}");
            }
            IsCardInitialized = false;
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
        }

        public ushort GetAxisAlarmCode(ushort card, ushort axis)
        {
            uint ioStatus = GetAxisIoStatus(card, axis);
            if ((ioStatus & 1u) == 0)
            {
                return 0;
            }
            ushort errorCode = 0;
            short result = LTDMC.nmc_get_axis_errcode(card, axis, ref errorCode);
            return result == 0 && errorCode != 0 ? errorCode : ushort.MaxValue;
        }

        public uint GetAxisIoStatus(ushort card, ushort axis)
        {
            return LTDMC.dmc_axis_io_status(card, axis);
        }

        //设置回零参数
        public void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode)
        {
            // 设置脉冲模式和原点逻辑由当前卡号参数决定。
            EnsureSuccess(LTDMC.dmc_set_homemode(card, axis, dir, speed, homeMode, 0), "设置回零模式", card, axis);

        }
        //启动回零
        public void StartHome(ushort card, ushort axis)
        { 
            EnsureSuccess(LTDMC.dmc_home_move(card, axis), "启动回零", card, axis);

        }

        //位置清零
        public void CleanPos(ushort card, ushort axis)
        {
            EnsureSuccess(LTDMC.dmc_set_position(card, axis, 0), "清零指令位置", card, axis);
        }
        //编码器位置清零
        public void CleanPosEncoder(ushort card, ushort axis)
        {
            EnsureSuccess(LTDMC.dmc_set_encoder(card, axis, 0), "清零编码器位置", card, axis);
        }

        public bool HomeStatus(ushort card, ushort axis) 
        {
            UInt16 result = 0;
            EnsureSuccess(LTDMC.dmc_get_home_result(card, axis, ref result), "读取回零结果", card, axis);
            if (result == 1)
            {
                return true;
            }
            else
            {
                return false;
            }
        }


        public bool SetIO(IO io, bool isOpen)
        {
            try
            {
                ushort b = (ushort)(isOpen == true ? 0 : 1);
                lock (ioOutputLock)
                {
                    return LTDMC.dmc_write_outbit((ushort)io.CardNum, ushort.Parse(io.IOIndex), b) == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
        {
            if (commands == null || commands.Count == 0)
            {
                return false;
            }

            try
            {
                int card = commands[0].Io.CardNum;
                var indexes = new HashSet<int>();
                foreach (IoOutputCommand command in commands)
                {
                    IO io = command?.Io;
                    if (io == null || io.CardNum != card || io.IOType != "通用输出"
                        || !int.TryParse(io.IOIndex, out int index) || index < 0 || index > 31
                        || !indexes.Add(index))
                    {
                        return false;
                    }
                }

                lock (ioOutputLock)
                {
                    uint outputValue = LTDMC.dmc_read_outport((ushort)card, 0);
                    foreach (IoOutputCommand command in commands)
                    {
                        int index = int.Parse(command.Io.IOIndex);
                        uint mask = 1u << index;
                        outputValue = command.TargetState
                            ? outputValue & ~mask
                            : outputValue | mask;
                    }
                    return LTDMC.dmc_write_outport((ushort)card, 0, outputValue) == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
        public bool GetOutIO(IO io, ref bool value)
        {
            try
            {
                value = LTDMC.dmc_read_outbit((ushort)io.CardNum, ushort.Parse(io.IOIndex)) == 1 ? false : true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }
        public bool GetInIO(IO io, ref bool value)
        {
            try
            {
                value = LTDMC.dmc_read_inbit((ushort)io.CardNum, ushort.Parse(io.IOIndex)) == 1 ? false : true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

        public void DownLoadConfig()
        {
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
            string folderPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\Config";
            string searchPattern = "card_*";
            if (Directory.Exists(folderPath))
            {
                string[] filePaths = Directory.GetFiles(folderPath, searchPattern);

                if (filePaths.Length > 0)
                {
                    foreach (string filePath in filePaths)
                    {
                        string fileName = Path.GetFileName(filePath);
                        Match match = Regex.Match(fileName, @"_(\d+)");
                        if (match.Success)
                        {
                            string numberString = match.Groups[1].Value;
                            int number;
                            if (int.TryParse(numberString, out number))
                            {
                                EnsureSuccess(LTDMC.dmc_download_configfile((ushort)number, filePath),
                                    "下载控制卡配置", (ushort)number, 0);
                            }
                        }
                      
                    }
                }
               
            }
        }

        public void SetAllAxisSevonOn()
        {
            for (int i = 0; i < cardStore.GetControlCardCount(); i++)
            {
        
                for (int j = 0; j < cardStore.GetAxisCount(i); j++)
                {
                    SetAxisSevon((ushort)i, (ushort)j, true);
                }

            }
        }

        public void SetAllAxisEquiv()
        {
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
            for (int i = 0; i < cardStore.GetControlCardCount(); i++)
            {

                for (int j = 0; j < cardStore.GetAxisCount(i); j++)
                {
                    if (cardStore.TryGetAxis(i, j, out Axis axisInfo))
                    {
                        EnsureSuccess(LTDMC.dmc_set_equiv((ushort)i, (ushort)j, axisInfo.PulseToMM),
                            "设置脉冲当量", (ushort)i, (ushort)j);
                    }
                }

            }
        }
        public void ResetAxisAlarm(ushort card, ushort axis)
        {
            EnsureSuccess(LTDMC.nmc_clear_axis_errcode(card, axis), "清除轴错误码", card, axis);
            EnsureSuccess(LTDMC.dmc_clear_stop_reason(card, axis), "清除轴停止原因", card, axis);
            EnsureSuccess(LTDMC.dmc_write_erc_pin(card, axis, 1), "置位驱动器复位信号", card, axis);
            EnsureSuccess(LTDMC.dmc_write_erc_pin(card, axis, 0), "复位驱动器复位信号", card, axis);
        }

        //读取当前速度
        public double GetAxisCurSpeed(ushort i, ushort j)
        {
            double Speed = 0;
            LTDMC.dmc_read_current_speed_unit(i, j,ref Speed);
            return Math.Round(Speed,3);
        }
    }
}
