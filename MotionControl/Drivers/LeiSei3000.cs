using System;
// 模块：运动控制 / 驱动。
// 职责范围：封装 3.0 NEtherCat 路线的雷赛 EtherCAT 总线卡 SDK 与具体硬件调用。
// 排查入口：本层负责把 LTDMC 返回码转成明确异常；参数来源和运动互斥问题应回到 MotionCtrl/ProcessEngine 排查。

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Automation.MotionControl;
using EtherCatLTDMC;

namespace Automation
{
    public class LS
    {
        private const ushort LogicalCardIndex = 0;
        private const ushort EtherCatChannel = 2;
        private const string CardConfigurationFileName = "card_0.ini";
        private const uint PositiveHardLimitMask = 1u << 1;
        private const uint NegativeHardLimitMask = 1u << 2;
        private const uint PositiveSoftLimitMask = 1u << 6;
        private const uint NegativeSoftLimitMask = 1u << 7;
        private const uint PositiveLimitMask = PositiveHardLimitMask | PositiveSoftLimitMask;
        private const uint NegativeLimitMask = NegativeHardLimitMask | NegativeSoftLimitMask;

        private readonly CardConfigStore cardStore;
        private readonly string configPath;
        private readonly object lifecycleLock = new object();
        private ushort realCardId;
        private bool boardOpened;
        private bool configDownloaded;

        public LS(CardConfigStore cardStore, string configPath)
        {
            this.cardStore = cardStore ?? throw new ArgumentNullException(nameof(cardStore));
            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException("雷赛总线卡配置目录不能为空。", nameof(configPath));
            }
            if (!Path.IsPathRooted(configPath))
            {
                throw new ArgumentException("雷赛总线卡配置目录必须是绝对路径。", nameof(configPath));
            }
            this.configPath = Path.GetFullPath(configPath);
        }

        public bool IsCardInitialized { get; private set; }
        public ushort RealCardId => IsCardInitialized
            ? realCardId
            : throw new InvalidOperationException("雷赛总线卡尚未初始化。");
        private readonly object profileLock = new object();
        private readonly object ioOutputLock = new object();
        private readonly object continuousPathLock = new object();
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

        private static void EnsureSuccess(short result, string operation, ushort logicalCard, ushort realCard, ushort axis)
        {
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"雷赛总线调用失败:{operation},逻辑卡{logicalCard},物理卡{realCard},轴{axis},错误码{result}");
            }
        }

        internal static int ResolvePointMotionDirection(
            double targetOrDistance,
            ushort positionMode,
            double currentPosition)
        {
            double displacement = positionMode == 0
                ? targetOrDistance
                : targetOrDistance - currentPosition;
            return displacement > 0 ? 1 : displacement < 0 ? -1 : 0;
        }

        internal static bool IsDirectionBlockedByLimit(uint ioStatus, int direction)
        {
            return direction > 0
                ? (ioStatus & PositiveLimitMask) != 0
                : direction < 0 && (ioStatus & NegativeLimitMask) != 0;
        }

        private static string DescribeBlockingLimit(uint ioStatus, int direction)
        {
            var limits = new List<string>();
            if (direction > 0)
            {
                if ((ioStatus & PositiveHardLimitMask) != 0)
                {
                    limits.Add("正硬限位");
                }
                if ((ioStatus & PositiveSoftLimitMask) != 0)
                {
                    limits.Add("正软限位");
                }
            }
            else if (direction < 0)
            {
                if ((ioStatus & NegativeHardLimitMask) != 0)
                {
                    limits.Add("负硬限位");
                }
                if ((ioStatus & NegativeSoftLimitMask) != 0)
                {
                    limits.Add("负软限位");
                }
            }
            return string.Join("、", limits);
        }

        private static void EnsureDirectionAllowed(
            ushort logicalCard,
            ushort physicalCard,
            ushort axis,
            uint ioStatus,
            int direction)
        {
            if (!IsDirectionBlockedByLimit(ioStatus, direction))
            {
                return;
            }
            throw new InvalidOperationException(
                $"轴已触发{DescribeBlockingLimit(ioStatus, direction)}，禁止继续向限位方向运动:"
                + $"逻辑卡{logicalCard},物理卡{physicalCard},轴{axis}");
        }

        private static void EnsurePointMotionDirectionAllowed(
            ushort logicalCard,
            ushort physicalCard,
            ushort axis,
            double targetOrDistance,
            ushort positionMode)
        {
            // 保留 3.0 的限位语义：只拒绝继续压向已触发限位的命令，反向退出必须可用。
            double currentPosition = 0;
            if (positionMode == 1)
            {
                EnsureSuccess(
                    LTDMC.dmc_get_position_unit(physicalCard, axis, ref currentPosition),
                    "读取限位方向基准位置",
                    logicalCard,
                    physicalCard,
                    axis);
            }
            int direction = ResolvePointMotionDirection(
                targetOrDistance,
                positionMode,
                currentPosition);
            uint ioStatus = LTDMC.dmc_axis_io_status(physicalCard, axis);
            EnsureDirectionAllowed(logicalCard, physicalCard, axis, ioStatus, direction);
        }

        private ushort ResolveCardId(ushort logicalCard)
        {
            if (logicalCard != LogicalCardIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalCard), "当前平台只配置一张雷赛总线卡，逻辑卡号必须为0。");
            }
            if (!IsCardInitialized)
            {
                throw new InvalidOperationException("雷赛总线卡尚未初始化。");
            }
            return realCardId;
        }

        private void ResetRuntimeState()
        {
            IsCardInitialized = false;
            configDownloaded = false;
            realCardId = 0;
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
        }

        public ushort InitCard()
        {
            lock (lifecycleLock)
            {
                return InitCardCore();
            }
        }

        private ushort InitCardCore()
        {
            if (IsCardInitialized)
            {
                return realCardId;
            }
            if (boardOpened)
            {
                LTDMC.dmc_board_close();
                boardOpened = false;
            }
            ResetRuntimeState();
            int configuredCardCount = cardStore.GetControlCardCount();
            if (configuredCardCount != 1)
            {
                throw new InvalidOperationException(
                    $"雷赛总线卡初始化要求配置且仅配置一张卡，当前配置数量:{configuredCardCount}。");
            }

            try
            {
                short detectedCardCount = InitializeBoard();
                boardOpened = detectedCardCount > 0;
                if (detectedCardCount == 0)
                {
                    throw new MotionCardUnavailableException(
                        "雷赛总线卡初始化失败，SDK实际发现0张卡。可继续编辑，但物理轴不可用。");
                }
                if (detectedCardCount != 1)
                {
                    throw new InvalidOperationException(
                        $"雷赛总线卡初始化失败，SDK实际发现{detectedCardCount}张卡，平台要求恰好1张。");
                }

                ushort returnedCardCount = 0;
                ushort[] cardIds = new ushort[8];
                uint[] cardTypes = new uint[8];
                short result = LTDMC.dmc_get_CardInfList(ref returnedCardCount, cardTypes, cardIds);
                if (result != 0 || returnedCardCount != 1)
                {
                    throw new InvalidOperationException(
                        $"读取雷赛总线卡信息失败，错误码:{result}，返回卡数量:{returnedCardCount}。");
                }

                realCardId = cardIds[0];
                EnsureEtherCatHealthy(realCardId);
                DownloadConfigurationCore();
                InitializeAxesCore();
                IsCardInitialized = true;
                return realCardId;
            }
            catch
            {
                if (boardOpened)
                {
                    LTDMC.dmc_board_close();
                    boardOpened = false;
                }
                ResetRuntimeState();
                throw;
            }
        }

        private static short InitializeBoard()
        {
            try
            {
                return LTDMC.dmc_board_init();
            }
            catch (DllNotFoundException ex)
            {
                throw CreateSdkUnavailableException(ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw CreateSdkUnavailableException(ex);
            }
            catch (BadImageFormatException ex)
            {
                throw CreateSdkUnavailableException(ex);
            }
        }

        private static MotionCardUnavailableException CreateSdkUnavailableException(
            Exception exception)
        {
            return new MotionCardUnavailableException(
                $"雷赛总线卡原生SDK不可用:{exception.Message}。可继续编辑，但物理轴不可用。",
                exception);
        }

        private static void EnsureEtherCatHealthy(ushort cardId)
        {
            ushort busError = 0;
            short result = LTDMC.nmc_get_errcode(cardId, EtherCatChannel, ref busError);
            if (result != 0)
            {
                throw new InvalidOperationException($"读取雷赛EtherCAT总线状态失败，物理卡{cardId}，错误码:{result}。");
            }
            if (busError == 0)
            {
                return;
            }

            EnsureSuccess(LTDMC.dmc_soft_reset(cardId), "总线异常后软复位", LogicalCardIndex, cardId, 0);
            // 3.0 现场代码验证该卡热复位需要较长恢复时间，不能缩短为普通轮询间隔。
            Thread.Sleep(10000);
            for (int retry = 0; retry < 10; retry++)
            {
                result = LTDMC.nmc_get_errcode(cardId, EtherCatChannel, ref busError);
                if (result == 0 && busError == 0)
                {
                    return;
                }
                Thread.Sleep(100);
            }
            throw new InvalidOperationException(
                $"雷赛EtherCAT总线连接异常，物理卡{cardId}，SDK错误码:{result}，总线错误码:{busError}。");
        }

        private void InitializeAxesCore()
        {
            int axisCount = cardStore.GetAxisCount(LogicalCardIndex);
            for (ushort axis = 0; axis < axisCount; axis++)
            {
                if (!cardStore.TryGetAxis(LogicalCardIndex, axis, out Axis axisInfo))
                {
                    throw new InvalidOperationException($"雷赛总线卡{axis}号轴配置不存在。");
                }
                EnsureSuccess(
                    LTDMC.dmc_set_equiv(realCardId, axis, axisInfo.PulseToMM),
                    "初始化脉冲当量",
                    LogicalCardIndex,
                    realCardId,
                    axis);

                if (axisInfo.EncoderType == AxisEncoderType.Absolute)
                {
                    InitializeAbsoluteEncoderAxis(axis);
                }
                if (axisInfo.NegativeSoftLimit != 0 || axisInfo.PositiveSoftLimit != 0)
                {
                    EnsureSuccess(
                        LTDMC.dmc_set_softlimit_unit(
                            realCardId,
                            axis,
                            1,
                            1,
                            1,
                            axisInfo.NegativeSoftLimit,
                            axisInfo.PositiveSoftLimit),
                        "设置轴软限位",
                        LogicalCardIndex,
                        realCardId,
                        axis);
                }

                uint ioStatus = LTDMC.dmc_axis_io_status(realCardId, axis);
                if ((ioStatus & 1u) == 0)
                {
                    EnableAxisCore(axis);
                }
            }
        }

        private void InitializeAbsoluteEncoderAxis(ushort axis)
        {
            ushort slaveAddress = 0;
            ushort subSlaveAddress = 0;
            EnsureSuccess(
                LTDMC.nmc_get_axis_node_address(realCardId, axis, ref slaveAddress, ref subSlaveAddress),
                "读取绝对值编码器轴节点地址",
                LogicalCardIndex,
                realCardId,
                axis);
            if (slaveAddress == 0)
            {
                throw new InvalidOperationException($"雷赛总线绝对值编码器轴节点地址无效:物理卡{realCardId},轴{axis}。");
            }

            double equiv = 0;
            EnsureSuccess(
                LTDMC.dmc_get_equiv(realCardId, axis, ref equiv),
                "读取绝对值编码器轴脉冲当量",
                LogicalCardIndex,
                realCardId,
                axis);
            if (double.IsNaN(equiv) || double.IsInfinity(equiv) || equiv <= 0)
            {
                throw new InvalidOperationException($"雷赛总线绝对值编码器轴脉冲当量无效:物理卡{realCardId},轴{axis},值{equiv}。");
            }

            int rawPosition = 0;
            EnsureSuccess(
                LTDMC.nmc_get_node_od(
                    realCardId,
                    EtherCatChannel,
                    slaveAddress,
                    0x6064,
                    0,
                    32,
                    ref rawPosition),
                "读取绝对值编码器位置",
                LogicalCardIndex,
                realCardId,
                axis);
            double position = rawPosition / equiv;
            if (double.IsNaN(position) || double.IsInfinity(position))
            {
                throw new InvalidOperationException($"雷赛总线绝对值编码器位置无效:物理卡{realCardId},轴{axis}。");
            }
            EnsureSuccess(
                LTDMC.dmc_set_encoder_unit(realCardId, axis, position),
                "同步绝对值编码器位置",
                LogicalCardIndex,
                realCardId,
                axis);
            EnsureSuccess(
                LTDMC.dmc_set_position_unit(realCardId, axis, position),
                "同步绝对值编码器指令位置",
                LogicalCardIndex,
                realCardId,
                axis);

        }

        private void EnableAxisCore(ushort axis)
        {
            ushort stateMachine = 0;
            EnsureSuccess(
                LTDMC.nmc_get_axis_state_machine(realCardId, axis, ref stateMachine),
                "读取轴状态机",
                LogicalCardIndex,
                realCardId,
                axis);
            if (stateMachine == 4)
            {
                return;
            }

            EnsureSuccess(
                LTDMC.nmc_clear_axis_errcode(realCardId, axis),
                "清除轴错误码",
                LogicalCardIndex,
                realCardId,
                axis);
            EnsureSuccess(
                LTDMC.dmc_set_factor_error(realCardId, axis, 1, 100),
                "设置轴跟随误差参数",
                LogicalCardIndex,
                realCardId,
                axis);
            EnsureSuccess(
                LTDMC.nmc_set_axis_enable(realCardId, axis),
                "轴自动上使能",
                LogicalCardIndex,
                realCardId,
                axis);
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
            ushort physicalCard = ResolveCardId(card);
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
             EnsureSuccess(LTDMC.dmc_set_equiv(physicalCard, axis, equiv), "设置脉冲当量", card, physicalCard, axis);

              EnsureSuccess(LTDMC.dmc_set_profile_unit(physicalCard, axis, minVel, dMaxVel, acc, dec, dStopVel), "设置速度参数", card, physicalCard, axis);
           // LTDMC.dmc_set_acc_profile(card, axis, minVel, dMaxVel* equiv, acc* equiv, dec* equiv, dStopVel);
          //  LTDMC.dmc_set_acc_profile(card, axis, minVel, dMaxVel, acc, dec, dStopVel);

             EnsureSuccess(LTDMC.dmc_set_s_profile(physicalCard, axis, 0, dS_para), "设置S曲线", card, physicalCard, axis);

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
            ushort physicalCard = ResolveCardId(card);
            EnsurePointMotionDirectionAllowed(card, physicalCard, axis, dDist, sPosi_mode);
            EnsureSuccess(LTDMC.dmc_pmove_unit(physicalCard, axis, dDist, sPosi_mode), "定长运动", card, physicalCard, axis);

            while (!GetInPos(card, axis) && wait)
            {
                Thread.Sleep(1);
            }
        }

        public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
        {
            if (request == null || request.Axes == null || request.Positions == null
                || request.Axes.Count == 0 || request.Axes.Count != request.Positions.Count
                || request.Axes.Count > 6
                || request.CoordinateSystem > CoordinatedLinearMoveRequest.MaximumCoordinateSystem
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
            ushort physicalCard = ResolveCardId(request.Card);
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
                EnsurePointMotionDirectionAllowed(
                    request.Card,
                    physicalCard,
                    axes[i],
                    positions[i],
                    request.PositionMode);
            }
            EnsureSuccess(LTDMC.dmc_set_vector_profile_unit(physicalCard, request.CoordinateSystem,
                0, request.MaxVelocity, request.AccelerationTime, request.DecelerationTime, 0),
                "设置协调直线运动参数", request.Card, physicalCard, request.CoordinateSystem);
            EnsureSuccess(LTDMC.dmc_set_vector_s_profile(physicalCard, request.CoordinateSystem, 0, 0),
                "设置协调直线S曲线", request.Card, physicalCard, request.CoordinateSystem);
            EnsureSuccess(LTDMC.dmc_line_unit(physicalCard, request.CoordinateSystem,
                (ushort)axes.Length, axes, positions, request.PositionMode),
                "启动协调直线运动", request.Card, physicalCard, request.CoordinateSystem);
        }

        public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
        {
            EnsureCoordinateSystemInRange(coordinateSystem);
            ushort physicalCard = ResolveCardId(card);
            short result = LTDMC.dmc_check_done_multicoor(physicalCard, coordinateSystem);
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
            EnsureCoordinateSystemInRange(coordinateSystem);
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(LTDMC.dmc_stop_multicoor(physicalCard, coordinateSystem, stopMode),
                "停止协调直线运动", card, physicalCard, coordinateSystem);
        }

        public void MoveContinuousPath(ContinuousPathMoveRequest request)
        {
            ValidateContinuousPathRequest(request);
            ushort[] axes = request.Axes.ToArray();
            ushort physicalCard = ResolveCardId(request.Card);
            foreach (ContinuousPathSegment segment in request.Segments)
            {
                ValidateContinuousPathLimits(request, segment, physicalCard);
            }

            lock (continuousPathLock)
            {
                short runState = LTDMC.dmc_conti_get_run_state(
                    physicalCard, request.CoordinateSystem);
                if (runState == 0 || runState == 1)
                {
                    throw new InvalidOperationException(
                        $"连续轨迹坐标系正在运行:卡{request.Card},坐标系{request.CoordinateSystem},状态{runState}");
                }
                if (runState == 3)
                {
                    EnsureSuccess(
                        LTDMC.dmc_conti_close_list(physicalCard, request.CoordinateSystem),
                        "关闭未启动的连续轨迹缓存区",
                        request.Card,
                        physicalCard,
                        request.CoordinateSystem);
                }

                ushort lookAheadEnabled = request.LookAheadEnabled ? (ushort)1 : (ushort)0;
                EnsureSuccess(
                    LTDMC.dmc_conti_set_lookahead_mode(
                        physicalCard,
                        request.CoordinateSystem,
                        lookAheadEnabled,
                        200,
                        request.PathError,
                        request.LookAheadAcceleration),
                    "设置连续轨迹前瞻",
                    request.Card,
                    physicalCard,
                    request.CoordinateSystem);
                EnsureSuccess(
                    LTDMC.dmc_conti_open_list(
                        physicalCard,
                        request.CoordinateSystem,
                        (ushort)axes.Length,
                        axes),
                    "打开连续轨迹缓存区",
                    request.Card,
                    physicalCard,
                    request.CoordinateSystem);

                bool listOpened = true;
                bool startAttempted = false;
                bool completed = false;
                try
                {
                    foreach (ContinuousPathSegment segment in request.Segments)
                    {
                        EnsureSuccess(
                            LTDMC.dmc_set_vector_s_profile(
                                physicalCard,
                                request.CoordinateSystem,
                                0,
                                segment.AccelerationTime / 5d),
                            "设置连续轨迹S曲线",
                            request.Card,
                            physicalCard,
                            request.CoordinateSystem);
                        EnsureSuccess(
                            LTDMC.dmc_set_vector_profile_unit(
                                physicalCard,
                                request.CoordinateSystem,
                                0,
                                segment.MaxVelocity,
                                segment.AccelerationTime,
                                segment.DecelerationTime,
                                segment.EndVelocity),
                            "设置连续轨迹速度",
                            request.Card,
                            physicalCard,
                            request.CoordinateSystem);
                        AddContinuousPathSegment(request, segment, physicalCard, axes);
                    }

                    startAttempted = true;
                    EnsureSuccess(
                        LTDMC.dmc_conti_start_list(physicalCard, request.CoordinateSystem),
                        "启动连续轨迹",
                        request.Card,
                        physicalCard,
                        request.CoordinateSystem);
                    EnsureSuccess(
                        LTDMC.dmc_conti_close_list(physicalCard, request.CoordinateSystem),
                        "关闭已启动的连续轨迹缓存区",
                        request.Card,
                        physicalCard,
                        request.CoordinateSystem);
                    listOpened = false;
                    completed = true;
                }
                finally
                {
                    // start/close 的返回状态不确定时先尽力停下该坐标系，避免失败后仍残留运动。
                    if (!completed && startAttempted)
                    {
                        LTDMC.dmc_conti_stop_list(
                            physicalCard, request.CoordinateSystem, 1);
                    }
                    if (listOpened)
                    {
                        LTDMC.dmc_conti_close_list(physicalCard, request.CoordinateSystem);
                    }
                }
            }
        }

        public bool IsContinuousPathDone(ushort card, ushort coordinateSystem)
        {
            EnsureCoordinateSystemInRange(coordinateSystem);
            ushort physicalCard = ResolveCardId(card);
            short result = LTDMC.dmc_conti_check_done(physicalCard, coordinateSystem);
            if (result == 0)
            {
                return false;
            }
            if (result == 1)
            {
                return true;
            }
            throw new InvalidOperationException(
                $"读取连续轨迹状态失败:卡{card},坐标系{coordinateSystem},返回值{result}");
        }

        public void StopContinuousPath(ushort card, ushort coordinateSystem, ushort stopMode)
        {
            EnsureCoordinateSystemInRange(coordinateSystem);
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(
                LTDMC.dmc_conti_stop_list(physicalCard, coordinateSystem, stopMode),
                "停止连续轨迹",
                card,
                physicalCard,
                coordinateSystem);
        }

        private static void ValidateContinuousPathRequest(ContinuousPathMoveRequest request)
        {
            if (request == null || request.Axes == null || request.Segments == null
                || request.Axes.Count == 0 || request.Axes.Count > 6
                || request.Axes.Distinct().Count() != request.Axes.Count
                || request.Segments.Count == 0
                || request.CoordinateSystem > CoordinatedLinearMoveRequest.MaximumCoordinateSystem
                || request.PositionMode != 1
                || request.PathError < 0 || double.IsNaN(request.PathError)
                || double.IsInfinity(request.PathError)
                || request.LookAheadAcceleration <= 0
                || double.IsNaN(request.LookAheadAcceleration)
                || double.IsInfinity(request.LookAheadAcceleration))
            {
                throw new ArgumentException("连续轨迹公共参数无效。", nameof(request));
            }

            foreach (ContinuousPathSegment segment in request.Segments)
            {
                if (segment == null
                    || !Enum.IsDefined(typeof(ContinuousPathSegmentType), segment.Type)
                    || !HasFinitePositions(segment.TargetPositions, request.Axes.Count)
                    || segment.MaxVelocity <= 0
                    || segment.AccelerationTime <= 0
                    || segment.DecelerationTime <= 0
                    || segment.EndVelocity < 0
                    || !IsFinite(segment.MaxVelocity)
                    || !IsFinite(segment.AccelerationTime)
                    || !IsFinite(segment.DecelerationTime)
                    || !IsFinite(segment.EndVelocity)
                    || segment.ArcDirection > 1)
                {
                    throw new ArgumentException("连续轨迹段参数无效。", nameof(request));
                }
                if (segment.Type == ContinuousPathSegmentType.ArcThreePoint
                    && (!HasFinitePositions(segment.StartPositions, request.Axes.Count)
                        || !HasFinitePositions(segment.MiddlePositions, request.Axes.Count)))
                {
                    throw new ArgumentException("三点圆弧必须提供完整的起点和中间点。", nameof(request));
                }
                if (segment.Type == ContinuousPathSegmentType.ArcCenter
                    && !HasFinitePositions(segment.MiddlePositions, request.Axes.Count))
                {
                    throw new ArgumentException("圆心圆弧必须提供完整圆心。", nameof(request));
                }
                if (segment.Type == ContinuousPathSegmentType.ArcRadius
                    && (!IsFinite(segment.Radius) || segment.Radius <= 0))
                {
                    throw new ArgumentException("半径圆弧的半径必须大于0。", nameof(request));
                }
            }
        }

        private static bool HasFinitePositions(IReadOnlyList<double> positions, int count)
        {
            return positions != null && positions.Count == count && positions.All(IsFinite);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void AddContinuousPathSegment(
            ContinuousPathMoveRequest request,
            ContinuousPathSegment segment,
            ushort physicalCard,
            ushort[] axes)
        {
            short result;
            double[] target = segment.TargetPositions.ToArray();
            switch (segment.Type)
            {
                case ContinuousPathSegmentType.Line:
                    result = LTDMC.dmc_conti_line_unit(
                        physicalCard,
                        request.CoordinateSystem,
                        (ushort)axes.Length,
                        axes,
                        target,
                        request.PositionMode,
                        0);
                    break;
                case ContinuousPathSegmentType.ArcThreePoint:
                    result = LTDMC.dmc_conti_arc_move_3points_unit(
                        physicalCard,
                        request.CoordinateSystem,
                        (ushort)axes.Length,
                        axes,
                        target,
                        segment.MiddlePositions.ToArray(),
                        segment.Circle,
                        request.PositionMode,
                        0);
                    break;
                case ContinuousPathSegmentType.ArcCenter:
                    result = LTDMC.dmc_conti_arc_move_center_unit(
                        physicalCard,
                        request.CoordinateSystem,
                        (ushort)axes.Length,
                        axes,
                        target,
                        segment.MiddlePositions.ToArray(),
                        segment.ArcDirection,
                        segment.Circle,
                        request.PositionMode,
                        0);
                    break;
                case ContinuousPathSegmentType.ArcRadius:
                    result = LTDMC.dmc_conti_arc_move_radius_unit(
                        physicalCard,
                        request.CoordinateSystem,
                        (ushort)axes.Length,
                        axes,
                        target,
                        segment.Radius,
                        segment.ArcDirection,
                        segment.Circle,
                        request.PositionMode,
                        0);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(segment.Type));
            }
            EnsureSuccess(
                result,
                $"添加连续轨迹段[{segment.Type}]",
                request.Card,
                physicalCard,
                request.CoordinateSystem);
        }

        private static void ValidateContinuousPathLimits(
            ContinuousPathMoveRequest request,
            ContinuousPathSegment segment,
            ushort physicalCard)
        {
            IEnumerable<IReadOnlyList<double>> positions = new[]
            {
                segment.StartPositions,
                segment.MiddlePositions,
                segment.TargetPositions
            }.Where(item => item != null);
            foreach (IReadOnlyList<double> point in positions)
            {
                for (int index = 0; index < request.Axes.Count; index++)
                {
                    EnsurePointMotionDirectionAllowed(
                        request.Card,
                        physicalCard,
                        request.Axes[index],
                        point[index],
                        request.PositionMode);
                }
            }
        }

        private static void EnsureCoordinateSystemInRange(ushort coordinateSystem)
        {
            if (coordinateSystem > CoordinatedLinearMoveRequest.MaximumCoordinateSystem)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coordinateSystem),
                    $"雷赛总线卡坐标系必须在0到{CoordinatedLinearMoveRequest.MaximumCoordinateSystem}之间。");
            }
        }
        //连续运动
        public void Jog(ushort card, ushort axis, ushort sDir)
        {
           // ushort sDir; //运动方向，0：负方向，1：正方向
             if (sDir > 1)
             {
                 throw new ArgumentOutOfRangeException(nameof(sDir), "连续运动方向必须为0（负向）或1（正向）。");
             }
             ushort physicalCard = ResolveCardId(card);
             int direction = sDir == 0 ? -1 : 1;
             uint ioStatus = LTDMC.dmc_axis_io_status(physicalCard, axis);
             EnsureDirectionAllowed(card, physicalCard, axis, ioStatus, direction);
             EnsureSuccess(LTDMC.dmc_vmove(physicalCard, axis, sDir), "连续运动", card, physicalCard, axis);
        }

        //制动
        public void StopOneAxis(ushort card, ushort axis, ushort stop_mode)
        {
            //stop_mode//制动方式，0：减速停止，1：紧急停止

             ushort physicalCard = ResolveCardId(card);
             EnsureSuccess(LTDMC.dmc_stop(physicalCard, axis, stop_mode), "停止轴", card, physicalCard, axis);
             Stopwatch timeout = Stopwatch.StartNew();
             while (timeout.ElapsedMilliseconds <= 500)
             {
                 short state = LTDMC.dmc_check_done(physicalCard, axis);
                 if (state == 1)
                 {
                     return;
                 }
                 if (state != 0)
                 {
                     throw new InvalidOperationException(
                         $"停止轴后读取停稳状态失败:逻辑卡{card},物理卡{physicalCard},轴{axis},返回值{state}");
                 }
                 Thread.Sleep(10);
             }
             throw new TimeoutException(
                 $"停止轴后500毫秒内未确认停稳:逻辑卡{card},物理卡{physicalCard},轴{axis}");
        }

        // 读取指定轴运动状态
        public bool GetInPos(ushort card,ushort axis) //检测轴是否到位
        {
            short result = LTDMC.dmc_check_done(ResolveCardId(card), axis);
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
            ushort physicalCard = ResolveCardId(card);
            ushort stateMachine = 0;
            EnsureSuccess(
                LTDMC.nmc_get_axis_state_machine(physicalCard, axis, ref stateMachine),
                "读取轴使能状态",
                card,
                physicalCard,
                axis);
            return stateMachine == 4;
        }
        // 设置指定轴使能状态
        public void SetAxisSevon(ushort card, ushort axis,bool isSevon)
        {
            ushort physicalCard = ResolveCardId(card);
            if (isSevon)
            {
                EnableAxisCore(axis);
                return;
            }
            EnsureSuccess(
                LTDMC.nmc_set_axis_disable(physicalCard, axis),
                "轴下使能",
                card,
                physicalCard,
                axis);
        }
        //读取指定轴的位置
        public double GetAxisPos(ushort card, ushort axis)
        {
            double pos = 0;
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(
                LTDMC.dmc_get_position_unit(physicalCard, axis, ref pos),
                "读取指令位置",
                card,
                physicalCard,
                axis);
            return pos;
        }
        //读取指定轴的编码器位置
        public double GetAxisPosEncoder(ushort card, ushort axis)
        {
            double pos = 0;
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(LTDMC.dmc_get_encoder_unit(physicalCard, axis, ref pos), "读取编码器位置", card, physicalCard, axis);
            return pos;
        }
        public void StopConnect()
        {
            lock (lifecycleLock)
            {
                if (!boardOpened)
                {
                    ResetRuntimeState();
                    return;
                }
                short result = LTDMC.dmc_board_close();//控制卡关闭函数，释放系统资源
                boardOpened = false;
                ResetRuntimeState();
                if (result != 0)
                {
                    throw new InvalidOperationException($"关闭雷赛总线卡失败:错误码{result}");
                }
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
            ushort physicalCard = ResolveCardId(card);
            short result = LTDMC.nmc_get_axis_errcode(physicalCard, axis, ref errorCode);
            return result == 0 && errorCode != 0 ? errorCode : ushort.MaxValue;
        }

        public uint GetAxisIoStatus(ushort card, ushort axis)
        {
            return LTDMC.dmc_axis_io_status(ResolveCardId(card), axis);
        }

        //设置回零参数
        public void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode)
        {
            ushort physicalCard = ResolveCardId(card);
            ushort configuredMode = 0;
            double lowVelocity = 0;
            double highVelocity = 0;
            double accelerationTime = 0;
            double decelerationTime = 0;
            double offsetPosition = 0;
            EnsureSuccess(
                LTDMC.nmc_get_home_profile(
                    physicalCard,
                    axis,
                    ref configuredMode,
                    ref lowVelocity,
                    ref highVelocity,
                    ref accelerationTime,
                    ref decelerationTime,
                    ref offsetPosition),
                "读取总线回原参数",
                card,
                physicalCard,
                axis);

            long key = ((long)card << 32) | axis;
            lock (profileLock)
            {
                if (appliedProfiles.TryGetValue(key, out MotionProfile profile))
                {
                    highVelocity = profile.MaxVel;
                    lowVelocity = profile.MaxVel * 0.8;
                    accelerationTime = profile.Acc;
                    decelerationTime = profile.Dec;
                }
            }
            if (highVelocity <= 0)
            {
                highVelocity = speed;
            }
            if (lowVelocity <= 0)
            {
                lowVelocity = highVelocity * 0.8;
            }
            if (accelerationTime <= 0)
            {
                accelerationTime = 0.5;
            }
            if (decelerationTime <= 0)
            {
                decelerationTime = 0.5;
            }
            ushort effectiveMode = homeMode > 0 ? homeMode : configuredMode;
            if (effectiveMode == 0)
            {
                throw new InvalidOperationException($"雷赛总线回原方法无效:逻辑卡{card},物理卡{physicalCard},轴{axis}。");
            }
            EnsureSuccess(
                LTDMC.nmc_set_home_profile(
                    physicalCard,
                    axis,
                    effectiveMode,
                    lowVelocity,
                    highVelocity,
                    accelerationTime,
                    decelerationTime,
                    offsetPosition),
                "设置总线回原参数",
                card,
                physicalCard,
                axis);
        }
        //启动回零
        public void StartHome(ushort card, ushort axis)
        { 
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(LTDMC.nmc_home_move(physicalCard, axis), "启动总线回原", card, physicalCard, axis);

        }

        // 3.0 的清零是不可拆分的设备语义：先清编码器，等待驱动刷新，再清指令位置。
        public void CleanPos(ushort card, ushort axis)
        {
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(LTDMC.dmc_set_encoder_unit(physicalCard, axis, 0), "清零编码器位置", card, physicalCard, axis);
            Thread.Sleep(100);
            EnsureSuccess(LTDMC.dmc_set_position_unit(physicalCard, axis, 0), "清零指令位置", card, physicalCard, axis);
        }

        public bool HomeStatus(ushort card, ushort axis) 
        {
            UInt16 result = 0;
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(LTDMC.dmc_get_home_result(physicalCard, axis, ref result), "读取回零结果", card, physicalCard, axis);
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
                if (!TryGetIoIndex(io, "通用输出", out ushort index)
                    || !TryMapLogicalIoState(io.EffectLevel, isOpen, out ushort hardwareValue))
                {
                    return false;
                }
                ushort physicalCard = ResolveCardId((ushort)io.CardNum);
                lock (ioOutputLock)
                {
                    return LTDMC.dmc_write_outbit(physicalCard, index, hardwareValue) == 0;
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
                if (commands[0]?.Io == null)
                {
                    return false;
                }
                int card = commands[0].Io.CardNum;
                ushort physicalCard = ResolveCardId((ushort)card);
                var hardwareValues = new Dictionary<int, ushort>();
                foreach (IoOutputCommand command in commands)
                {
                    IO io = command?.Io;
                    if (io == null || io.CardNum != card
                        || !TryGetIoIndex(io, "通用输出", out ushort parsedIndex)
                        || parsedIndex > 31
                        || hardwareValues.ContainsKey(parsedIndex)
                        || !TryMapLogicalIoState(io.EffectLevel, command.TargetState, out ushort hardwareValue))
                    {
                        return false;
                    }
                    hardwareValues.Add(parsedIndex, hardwareValue);
                }

                lock (ioOutputLock)
                {
                    uint outputValue = LTDMC.dmc_read_outport(physicalCard, 0);
                    foreach (KeyValuePair<int, ushort> item in hardwareValues)
                    {
                        uint mask = 1u << item.Key;
                        outputValue = item.Value == 0
                            ? outputValue & ~mask
                            : outputValue | mask;
                    }
                    return LTDMC.dmc_write_outport(physicalCard, 0, outputValue) == 0;
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
                if (!TryGetIoIndex(io, "通用输出", out ushort index))
                {
                    return false;
                }
                ushort physicalCard = ResolveCardId((ushort)io.CardNum);
                short hardwareValue = LTDMC.dmc_read_outbit(physicalCard, index);
                if (!TryMapHardwareIoState(io.EffectLevel, hardwareValue, out bool logicalValue))
                {
                    return false;
                }
                value = logicalValue;
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
                if (!TryGetIoIndex(io, "通用输入", out ushort index))
                {
                    return false;
                }
                ushort physicalCard = ResolveCardId((ushort)io.CardNum);
                short hardwareValue = LTDMC.dmc_read_inbit(physicalCard, index);
                if (!TryMapHardwareIoState(io.EffectLevel, hardwareValue, out bool logicalValue))
                {
                    return false;
                }
                value = logicalValue;
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

        internal static bool TryGetIoIndex(IO io, string expectedType, out ushort index)
        {
            index = 0;
            return io != null
                && io.CardNum == LogicalCardIndex
                // 当前总线卡契约把 IO 映射为卡内扁平位号，不支持用 Module 暗示扩展从站。
                && io.Module == 0
                && string.Equals(io.IOType, expectedType, StringComparison.Ordinal)
                && ushort.TryParse(io.IOIndex, out index);
        }

        internal static bool TryMapLogicalIoState(
            string effectLevel,
            bool logicalValue,
            out ushort hardwareValue)
        {
            hardwareValue = 0;
            if (string.Equals(effectLevel, "正常", StringComparison.Ordinal))
            {
                hardwareValue = logicalValue ? (ushort)1 : (ushort)0;
                return true;
            }
            if (string.Equals(effectLevel, "取反", StringComparison.Ordinal))
            {
                hardwareValue = logicalValue ? (ushort)0 : (ushort)1;
                return true;
            }
            return false;
        }

        internal static bool TryMapHardwareIoState(
            string effectLevel,
            short hardwareValue,
            out bool logicalValue)
        {
            logicalValue = false;
            if (hardwareValue != 0 && hardwareValue != 1)
            {
                return false;
            }
            if (string.Equals(effectLevel, "正常", StringComparison.Ordinal))
            {
                logicalValue = hardwareValue == 1;
                return true;
            }
            if (string.Equals(effectLevel, "取反", StringComparison.Ordinal))
            {
                logicalValue = hardwareValue == 0;
                return true;
            }
            return false;
        }

        public void DownLoadConfig()
        {
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
            ResolveCardId(LogicalCardIndex);
            if (!configDownloaded)
            {
                DownloadConfigurationCore();
            }
        }

        private void DownloadConfigurationCore()
        {
            string filePath = EnsureCardConfigurationFile();
            EnsureSuccess(
                LTDMC.dmc_download_configfile(realCardId, filePath),
                "下载控制卡配置",
                LogicalCardIndex,
                realCardId,
                0);
            configDownloaded = true;
        }

        private string EnsureCardConfigurationFile()
        {
            string targetPath = Path.Combine(configPath, CardConfigurationFileName);
            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            string assemblyDirectory = Path.GetDirectoryName(typeof(LS).Assembly.Location)
                ?? throw new InvalidOperationException("无法确定程序目录，不能部署雷赛总线卡默认配置。");
            string sourcePath = Path.Combine(
                assemblyDirectory,
                "Assets",
                "MotionControl",
                "LeiSai",
                CardConfigurationFileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("雷赛总线卡默认配置资产不存在。", sourcePath);
            }
            Directory.CreateDirectory(configPath);
            File.Copy(sourcePath, targetPath, false);
            return targetPath;
        }

        public void SetAllAxisSevonOn()
        {
            ResolveCardId(LogicalCardIndex);
            for (ushort axis = 0; axis < cardStore.GetAxisCount(LogicalCardIndex); axis++)
            {
                uint ioStatus = LTDMC.dmc_axis_io_status(realCardId, axis);
                if ((ioStatus & 1u) == 0)
                {
                    EnableAxisCore(axis);
                }
            }
        }

        public void SetAllAxisEquiv()
        {
            lock (profileLock)
            {
                appliedProfiles.Clear();
            }
            ResolveCardId(LogicalCardIndex);
            for (ushort axis = 0; axis < cardStore.GetAxisCount(LogicalCardIndex); axis++)
            {
                if (cardStore.TryGetAxis(LogicalCardIndex, axis, out Axis axisInfo))
                {
                    EnsureSuccess(
                        LTDMC.dmc_set_equiv(realCardId, axis, axisInfo.PulseToMM),
                        "设置脉冲当量",
                        LogicalCardIndex,
                        realCardId,
                        axis);
                }
            }
        }
        public void ResetAxisAlarm(ushort card, ushort axis)
        {
            ushort physicalCard = ResolveCardId(card);
            EnsureSuccess(LTDMC.nmc_clear_axis_errcode(physicalCard, axis), "清除轴错误码", card, physicalCard, axis);
            EnsureSuccess(LTDMC.dmc_clear_stop_reason(physicalCard, axis), "清除轴停止原因", card, physicalCard, axis);
        }

        //读取当前速度
        public double GetAxisCurSpeed(ushort i, ushort j)
        {
            double Speed = 0;
            ushort physicalCard = ResolveCardId(i);
            EnsureSuccess(
                LTDMC.dmc_read_current_speed_unit(physicalCard, j, ref Speed),
                "读取当前速度",
                i,
                physicalCard,
                j);
            return Math.Round(Speed,3);
        }
    }
}
