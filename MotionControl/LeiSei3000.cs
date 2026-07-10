using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using csLTDMC;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static Automation.FrmCard;

namespace Automation
{
    public class LS
    {
        public bool IsCardInitialized { get; private set; }

        private static void EnsureSuccess(short result, string operation, ushort card, ushort axis)
        {
            if (result != 0)
            {
                throw new InvalidOperationException($"运动控制调用失败:{operation},卡{card},轴{axis},错误码{result}");
            }
        }
        public ushort InitCard()
        {
            IsCardInitialized = true;
            short num = LTDMC.dmc_board_init();//获取卡数量
            if (num <= 0 || num > 8)
            {
                IsCardInitialized = false;
                SF.frmInfo.PrintInfo("获取卡信息失败",FrmInfo.Level.Error);
            }
            ushort _num = 0;
            ushort[] cardids = new ushort[8];
            uint[] cardtypes = new uint[8];
            short res = LTDMC.dmc_get_CardInfList(ref _num, cardtypes, cardids);
            if (res != 0)
            {
                IsCardInitialized = false;
                SF.frmInfo.PrintInfo("获取卡信息失败", FrmInfo.Level.Error);
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
                || double.IsNaN(dMaxVel) || double.IsInfinity(dMaxVel)
                || double.IsNaN(acc) || double.IsInfinity(acc)
                || double.IsNaN(dec) || double.IsInfinity(dec))
            {
                throw new ArgumentOutOfRangeException(nameof(dMaxVel), $"运动参数无效:卡{card},轴{axis}");
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
                Application.DoEvents();
                Thread.Sleep(1);
            }
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

        //设置指定轴的当前指令位置值
        public void SetPosition(ushort card, ushort axis, double dpos)
        {
            LTDMC.dmc_set_position_unit(card, axis, dpos); 
        }
        // 读取轴当前速度
        public double GetAxisSpeed(ushort card, ushort axis)
        {
            double dcurrent_speed = 0;//速度值
           
            LTDMC.dmc_read_current_speed_unit(card, axis, ref dcurrent_speed); // 读取轴当前速度

            return dcurrent_speed;
        
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
            return LTDMC.dmc_read_sevon_pin(card, axis) == 0 ? true : false;
        }
        // 设置指定轴使能状态
        public void SetAxisSevon(ushort card, ushort axis,bool isSevon)
        {
            ushort temp = isSevon ? (ushort)0 : (ushort)1;
             EnsureSuccess(LTDMC.dmc_write_sevon_pin(card,axis, temp), "设置轴使能", card, axis);
        }
        //读取指定轴的脉冲值
        public int GetAxisPulse(ushort card, ushort axis)
        {
          return LTDMC.dmc_get_position(card, axis);
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
        }

        //设置回零参数
        public void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode)
        {
            //LTDMC.dmc_set_pulse_outmode(SF.motion._CardID, axis, 0);//设置脉冲模式
            //LTDMC.dmc_set_home_pin_logic(SF.motion._CardID, axis, 0, 0);//设置原点低电平有效
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
            LTDMC.dmc_set_position(card, axis, 0);
        }
        //编码器位置清零
        public void CleanPosEncoder(ushort card, ushort axis)
        {
            LTDMC.dmc_set_encoder(card, axis, 0);
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
                LTDMC.dmc_write_outbit((ushort)io.CardNum, ushort.Parse(io.IOIndex), b); 
                return true;
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
            for (int i = 0; i < SF.cardStore.GetControlCardCount(); i++)
            {
        
                for (int j = 0; j < SF.cardStore.GetAxisCount(i); j++)
                {
                    SetAxisSevon((ushort)i, (ushort)j, true);
                }

            }
        }

        public void SetAllAxisEquiv()
        {
            for (int i = 0; i < SF.cardStore.GetControlCardCount(); i++)
            {

                for (int j = 0; j < SF.cardStore.GetAxisCount(i); j++)
                {
                    if (SF.cardStore.TryGetAxis(i, j, out Axis axisInfo))
                    {
                        EnsureSuccess(LTDMC.dmc_set_equiv((ushort)i, (ushort)j, axisInfo.PulseToMM),
                            "设置脉冲当量", (ushort)i, (ushort)j);
                    }
                }

            }
        }
        //清楚报警
        public void CleanAlarm()
        {
            for (int i = 0; i < SF.cardStore.GetControlCardCount(); i++)
            {

                for (int j = 0; j < SF.cardStore.GetAxisCount(i); j++)
                {
                    LTDMC.nmc_clear_axis_errcode((ushort)i, (ushort)j);
                    LTDMC.dmc_clear_stop_reason((ushort)i, (ushort)j);
                    LTDMC.dmc_write_erc_pin((ushort)i, (ushort)j, 1);
                    LTDMC.dmc_write_erc_pin((ushort)i, (ushort)j, 0);

                }

            }
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
