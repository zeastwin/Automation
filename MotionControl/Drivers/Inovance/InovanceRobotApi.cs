// 模块：运动控制 / 汇川机器人原生 API。
// 职责范围：把两代 IMC100 P/Invoke 的结构体差异收敛成可替换的函数表，便于无硬件验证。

using System;
using V3Api = IMC100APIDLL.IMC100API;
using V3Pose = IMC100APIDLL.ROBOT_POS;
using V4Api = IMC100APIV4DLL.IMC100API;
using V4MoveIo = IMC100APIV4DLL.MOV_IO;
using V4Pose = IMC100APIV4DLL.ROB_POS;
using V4Velocity = IMC100APIV4DLL.VER_DATA;

namespace Automation.MotionControl
{
    internal sealed class InovanceRobotPose
    {
        public InovanceRobotPose()
        {
            Coordinates = new double[6];
            ArmParameters = new int[4];
        }

        public double[] Coordinates { get; }

        public int[] ArmParameters { get; }

        public InovanceRobotPose Clone()
        {
            var clone = new InovanceRobotPose();
            Array.Copy(Coordinates, clone.Coordinates, Coordinates.Length);
            Array.Copy(ArmParameters, clone.ArmParameters, ArmParameters.Length);
            return clone;
        }
    }

    /// <summary>
    /// SDK 函数表只隔离原生调用，业务语义仍直接由工站实现。
    /// </summary>
    internal interface IInovanceRobotApi
    {
        int Initialize(uint address, ushort port, int timeoutMs, int connectionId);

        int Exit(int connectionId);

        int AcquirePermit(int command, int connectionId);

        int UserLogin(int type, byte[] password, int connectionId);

        int GetEmergencyStopStatus(out int status, int connectionId);

        int EmergencyStop(int command, int connectionId);

        int GetSystemError(out int error, int connectionId);

        int ResetError(int connectionId);

        int SetCoordinate(int type, int connectionId);

        int SetMode(int mode, int connectionId);

        int GetMotorStatus(out int status, int connectionId);

        int MotorEnable(int command, int connectionId);

        int GetDataStreamMode(out int mode, int connectionId);

        int SetDataStreamMode(int command, int connectionId);

        int SetSlewMode(int command, int connectionId);

        int GetMotionStatus(out int status, int connectionId);

        int GetPosition(out InovanceRobotPose position, int connectionId);

        int GetPoint(int pointIndex, out InovanceRobotPose position, int connectionId);

        int SetPoint(int pointIndex, InovanceRobotPose position, int connectionId);

        int ClearPallet(int connectionId);

        int SetPalletParameters(int rowCount, int columnCount, int connectionId);

        int GetPalletPoint(InovanceRobotPose point1, InovanceRobotPose point2,
            InovanceRobotPose point3, int rowIndex, int columnIndex,
            out InovanceRobotPose position, int connectionId);

        int MovePoint(int pointIndex, bool linear, int speed, int zone, int connectionId);

        int MovePosition(InovanceRobotPose position, bool linear, int speed, int zone, int connectionId);

        int SetRapidMove(int moveType, int enabled, int connectionId);

        int SetVelocity(int velocity, int connectionId);

        int SetAcceleration(double acceleration, double deceleration, int connectionId);

        int SetInchMode(int command, int connectionId);

        int SetStepMotion(float linearStep, float rotaryStep, int connectionId);

        int SetInchStep(int stepType, int connectionId);

        int Jog(int vendorAxis, int command, int connectionId);

        int Inch(int vendorAxis, int command, int connectionId);

        void Delay(int milliseconds);
    }

    internal sealed class NativeInovanceRobotApi : IInovanceRobotApi
    {
        public int Initialize(uint address, ushort port, int timeoutMs, int connectionId) =>
            V3Api.IMC100_Init_ETH(address, port, timeoutMs, connectionId);

        public int Exit(int connectionId) => V3Api.IMC100_Exit_ETH(connectionId);

        public int AcquirePermit(int command, int connectionId) => V3Api.IMC100_AcqPermit(command, connectionId);

        public int UserLogin(int type, byte[] password, int connectionId) =>
            V3Api.IMC100_UserLogin(type, password, connectionId);

        public int GetEmergencyStopStatus(out int status, int connectionId)
        {
            status = 0;
            return V3Api.IMC100_Get_EStopSts(ref status, connectionId);
        }

        public int EmergencyStop(int command, int connectionId) => V3Api.IMC100_EmergStop(command, connectionId);

        public int GetSystemError(out int error, int connectionId)
        {
            error = 0;
            return V3Api.IMC100_Get_SysErr(ref error, connectionId);
        }

        public int ResetError(int connectionId) => V3Api.IMC100_ResetErr(connectionId);

        public int SetCoordinate(int type, int connectionId) => V3Api.IMC100_Set_Coord(type, connectionId);

        public int SetMode(int mode, int connectionId) => V3Api.IMC100_Set_Mode(mode, connectionId);

        public int GetMotorStatus(out int status, int connectionId)
        {
            status = 0;
            return V3Api.IMC100_Get_MotorSts(ref status, connectionId);
        }

        public int MotorEnable(int command, int connectionId) => V3Api.IMC100_MotorEnable(command, connectionId);

        public int GetDataStreamMode(out int mode, int connectionId)
        {
            mode = 0;
            return V3Api.IMC100_Get_DsMode(ref mode, connectionId);
        }

        public int SetDataStreamMode(int command, int connectionId) => V3Api.IMC100_DsMode(command, connectionId);

        public int SetSlewMode(int command, int connectionId) => V3Api.IMC100_Set_SlewMode(command, connectionId);

        public int GetMotionStatus(out int status, int connectionId)
        {
            status = 0;
            return V3Api.IMC100_Get_MotionSts(ref status, connectionId);
        }

        public int GetPosition(out InovanceRobotPose position, int connectionId)
        {
            V3Pose native = CreateV3Pose();
            int result = V3Api.IMC100_Get_PosHere(ref native, connectionId);
            position = FromV3(native);
            return result;
        }

        public int GetPoint(int pointIndex, out InovanceRobotPose position, int connectionId)
        {
            V3Pose native = CreateV3Pose();
            int result = V3Api.IMC100_Get_P(pointIndex, ref native, connectionId);
            position = FromV3(native);
            return result;
        }

        public int SetPoint(int pointIndex, InovanceRobotPose position, int connectionId)
        {
            V3Pose native = ToV3(position);
            return V3Api.IMC100_Set_P(pointIndex, native, connectionId);
        }

        public int ClearPallet(int connectionId) => V3Api.IMC100_Clear_PalletPara(connectionId);

        public int SetPalletParameters(int rowCount, int columnCount, int connectionId) =>
            V3Api.IMC100_Set_PalletPara(rowCount, columnCount, 1, 0, connectionId);

        public int GetPalletPoint(InovanceRobotPose point1, InovanceRobotPose point2,
            InovanceRobotPose point3, int rowIndex, int columnIndex,
            out InovanceRobotPose position, int connectionId)
        {
            V3Pose nativePoint1 = ToV3(point1);
            V3Pose nativePoint2 = ToV3(point2);
            V3Pose nativePoint3 = ToV3(point3);
            V3Pose nativePosition = CreateV3Pose();
            int result = V3Api.IMC100_Get_PalletPoint(
                nativePoint1, nativePoint2, nativePoint3,
                rowIndex, columnIndex, 0, ref nativePosition, connectionId);
            position = FromV3(nativePosition);
            return result;
        }

        public int MovePoint(int pointIndex, bool linear, int speed, int zone, int connectionId) => linear
            ? V3Api.IMC100_MovL_P(pointIndex, speed, zone, connectionId)
            : V3Api.IMC100_MovJ_P(pointIndex, speed, zone, connectionId);

        public int MovePosition(InovanceRobotPose position, bool linear, int speed, int zone, int connectionId)
        {
            V3Pose native = ToV3(position);
            return linear
                ? V3Api.IMC100_MovL2(native, speed, zone, connectionId)
                : V3Api.IMC100_MovJ2(native, speed, zone, connectionId);
        }

        public int SetRapidMove(int moveType, int enabled, int connectionId) =>
            V3Api.IMC100_Set_RapidMove(moveType, enabled, connectionId);

        public int SetVelocity(int velocity, int connectionId) => V3Api.IMC100_Set_Vel(velocity, connectionId);

        public int SetAcceleration(double acceleration, double deceleration, int connectionId) =>
            V3Api.IMC100_Set_AccRamp(acceleration, deceleration, connectionId);

        public int SetInchMode(int command, int connectionId) => V3Api.IMC100_InchMode(command, connectionId);

        public int SetStepMotion(float linearStep, float rotaryStep, int connectionId)
        {
            int result = V3Api.IMC100_Set_StepMotionL(linearStep, connectionId);
            if (result != 0)
            {
                return result;
            }
            return V3Api.IMC100_Set_StepMotionR(rotaryStep, connectionId);
        }

        public int SetInchStep(int stepType, int connectionId) => V3Api.IMC100_Set_InchStep(stepType, connectionId);

        public int Jog(int vendorAxis, int command, int connectionId) =>
            V3Api.IMC100_Jog(1, vendorAxis, command, connectionId);

        public int Inch(int vendorAxis, int command, int connectionId) =>
            V3Api.IMC100_Inch(1, vendorAxis, command, connectionId);

        public void Delay(int milliseconds) => System.Threading.Thread.Sleep(milliseconds);

        private static V3Pose CreateV3Pose()
        {
            return new V3Pose
            {
                pos = new double[6],
                armType = new int[4]
            };
        }

        private static InovanceRobotPose FromV3(V3Pose source)
        {
            var result = new InovanceRobotPose();
            if (source.pos != null)
            {
                Array.Copy(source.pos, result.Coordinates, Math.Min(source.pos.Length, result.Coordinates.Length));
            }
            if (source.armType != null)
            {
                Array.Copy(source.armType, result.ArmParameters,
                    Math.Min(source.armType.Length, result.ArmParameters.Length));
            }
            return result;
        }

        private static V3Pose ToV3(InovanceRobotPose source)
        {
            V3Pose result = CreateV3Pose();
            Array.Copy(source.Coordinates, result.pos, result.pos.Length);
            Array.Copy(source.ArmParameters, result.armType, result.armType.Length);
            return result;
        }
    }

    internal sealed class NativeInovanceV4RobotApi : IInovanceRobotApi
    {
        public int Initialize(uint address, ushort port, int timeoutMs, int connectionId) =>
            V4Api.IMC100_Init_ETH(address, port, timeoutMs, connectionId);

        public int Exit(int connectionId) => V4Api.IMC100_Exit_ETH(connectionId);

        public int AcquirePermit(int command, int connectionId) => V4Api.IMC100_AcqPermit(command, connectionId);

        public int UserLogin(int type, byte[] password, int connectionId) =>
            V4Api.IMC100_UserLogin(type, password, connectionId);

        public int GetEmergencyStopStatus(out int status, int connectionId)
        {
            status = 0;
            return V4Api.IMC100_Get_EStopSts(ref status, connectionId);
        }

        public int EmergencyStop(int command, int connectionId) => V4Api.IMC100_EmergStop(command, connectionId);

        public int GetSystemError(out int error, int connectionId)
        {
            error = 0;
            return V4Api.IMC100_Get_SysErr(ref error, connectionId);
        }

        public int ResetError(int connectionId) => V4Api.IMC100_ResetErr(connectionId);

        public int SetCoordinate(int type, int connectionId) => V4Api.IMC100_Set_CoordType(type, connectionId);

        public int SetMode(int mode, int connectionId) => V4Api.IMC100_Set_Mode(mode, connectionId);

        public int GetMotorStatus(out int status, int connectionId)
        {
            status = 0;
            return V4Api.IMC100_Get_MotorSts(ref status, connectionId);
        }

        public int MotorEnable(int command, int connectionId) => V4Api.IMC100_MotorEnable(command, connectionId);

        public int GetDataStreamMode(out int mode, int connectionId)
        {
            mode = 0;
            return V4Api.IMC100_Get_DsMode(ref mode, connectionId);
        }

        public int SetDataStreamMode(int command, int connectionId) => V4Api.IMC100_DsMode(command, connectionId);

        public int SetSlewMode(int command, int connectionId) => V4Api.IMC100_Set_SlewMode(command, connectionId);

        public int GetMotionStatus(out int status, int connectionId)
        {
            status = 0;
            return V4Api.IMC100_Get_MotionSts(ref status, connectionId);
        }

        public int GetPosition(out InovanceRobotPose position, int connectionId)
        {
            V4Pose native = CreateV4Pose();
            int result = V4Api.IMC100_Get_RobPosHere(ref native, connectionId);
            position = FromV4(native);
            return result;
        }

        public int GetPoint(int pointIndex, out InovanceRobotPose position, int connectionId)
        {
            V4Pose native = CreateV4Pose();
            int result = V4Api.IMC100_Get_RobP(pointIndex, ref native, connectionId);
            position = FromV4(native);
            return result;
        }

        public int SetPoint(int pointIndex, InovanceRobotPose position, int connectionId)
        {
            V4Pose native = ToV4(position);
            return V4Api.IMC100_Set_RobP(pointIndex, ref native, connectionId);
        }

        public int ClearPallet(int connectionId) => V4Api.IMC100_Clear_PalletPara(connectionId);

        public int SetPalletParameters(int rowCount, int columnCount, int connectionId) =>
            V4Api.IMC100_Set_PalletPara(rowCount, columnCount, 1, 0, connectionId);

        public int GetPalletPoint(InovanceRobotPose point1, InovanceRobotPose point2,
            InovanceRobotPose point3, int rowIndex, int columnIndex,
            out InovanceRobotPose position, int connectionId)
        {
            V4Pose nativePoint1 = ToV4(point1);
            V4Pose nativePoint2 = ToV4(point2);
            V4Pose nativePoint3 = ToV4(point3);
            V4Pose nativePosition = CreateV4Pose();
            int result = V4Api.IMC100_Get_Pallet_RobP(
                ref nativePoint1, ref nativePoint2, ref nativePoint3,
                rowIndex, columnIndex, 0, ref nativePosition, connectionId);
            position = FromV4(nativePosition);
            return result;
        }

        public int MovePoint(int pointIndex, bool linear, int speed, int zone, int connectionId) => linear
            ? V4Api.IMC100_MovL_P(pointIndex, speed, zone, connectionId)
            : V4Api.IMC100_MovJ_P(pointIndex, speed, zone, connectionId);

        public int MovePosition(InovanceRobotPose position, bool linear, int speed, int zone, int connectionId)
        {
            V4Pose native = ToV4(position);
            var velocity = new V4Velocity { velPercent = speed };
            var moveIo = new V4MoveIo();
            return linear
                ? V4Api.IMC100_MovL_RobPos(ref native, ref velocity, zone, 0, ref moveIo, connectionId)
                : V4Api.IMC100_MovJ_RobPos(ref native, ref velocity, zone, 0, ref moveIo, connectionId);
        }

        public int SetRapidMove(int moveType, int enabled, int connectionId) =>
            V4Api.IMC100_Set_RapidMove(moveType, enabled, connectionId);

        public int SetVelocity(int velocity, int connectionId) => V4Api.IMC100_Set_Vel(velocity, connectionId);

        public int SetAcceleration(double acceleration, double deceleration, int connectionId) =>
            V4Api.IMC100_Set_AccRamp(acceleration, deceleration, connectionId);

        public int SetInchMode(int command, int connectionId) => V4Api.IMC100_InchMode(command, connectionId);

        public int SetStepMotion(float linearStep, float rotaryStep, int connectionId)
        {
            int result = V4Api.IMC100_Set_StepMotionL(linearStep, connectionId);
            if (result != 0)
            {
                return result;
            }
            return V4Api.IMC100_Set_StepMotionR(rotaryStep, connectionId);
        }

        public int SetInchStep(int stepType, int connectionId) => V4Api.IMC100_Set_InchStep(stepType, connectionId);

        public int Jog(int vendorAxis, int command, int connectionId) =>
            V4Api.IMC100_AxisJog(vendorAxis, command, connectionId);

        public int Inch(int vendorAxis, int command, int connectionId) =>
            V4Api.IMC100_AxisInch(vendorAxis, command, connectionId);

        public void Delay(int milliseconds) => System.Threading.Thread.Sleep(milliseconds);

        private static V4Pose CreateV4Pose()
        {
            return new V4Pose
            {
                RPosData = new double[6],
                ArmParm = new int[4],
                EPosData = new double[6]
            };
        }

        private static InovanceRobotPose FromV4(V4Pose source)
        {
            var result = new InovanceRobotPose();
            if (source.RPosData != null)
            {
                Array.Copy(source.RPosData, result.Coordinates,
                    Math.Min(source.RPosData.Length, result.Coordinates.Length));
            }
            if (source.ArmParm != null)
            {
                Array.Copy(source.ArmParm, result.ArmParameters,
                    Math.Min(source.ArmParm.Length, result.ArmParameters.Length));
            }
            return result;
        }

        private static V4Pose ToV4(InovanceRobotPose source)
        {
            V4Pose result = CreateV4Pose();
            Array.Copy(source.Coordinates, result.RPosData, result.RPosData.Length);
            Array.Copy(source.ArmParameters, result.ArmParm, result.ArmParm.Length);
            return result;
        }
    }
}
