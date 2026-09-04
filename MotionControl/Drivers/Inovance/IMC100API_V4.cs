using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

#pragma warning disable 1591

namespace IMC100APIV4DLL  //命名空间可根据应用程序修改
{
	[StructLayout(LayoutKind.Sequential)]
	public struct ROB_POS
	{
	    [MarshalAs(UnmanagedType.ByValArray, SizeConst=6)]
		public Double[] RPosData;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=4)]
		public Int32[] ArmParm;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=6)]
		public Double[] EPosData;				          
	}	
	
	[StructLayout(LayoutKind.Sequential)]
	public struct ROB_JPOS
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=8)]
		public Double[] JointData;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=6)]
		public Double[] EPosData;
	}	
	
	[StructLayout(LayoutKind.Sequential)]
	public struct POSE
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=6)]
		public Double[] Data;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct LOAD_DATA
	{
		public double Mass;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=3)]
		public Double[] Cog;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=3)]
		public Double[] Orient;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=3)]
		public Double[] Inertia;
	}
	
	[StructLayout(LayoutKind.Sequential)]
	public struct TOOL_DATA
	{
		public Int32 RobHold;
	  public POSE TFrame;
	  public LOAD_DATA TLoad;
  }
   
  [StructLayout(LayoutKind.Sequential)]
	public struct WOBJ_DATA
	{
	  public Int32 RobHold;
		public Int32 UFFix;
		[MarshalAs(UnmanagedType.ByValArray, SizeConst=18)]
		public char[] UFMec;
		public POSE UFrame;
		public POSE OFrame;
   }
   
  [StructLayout(LayoutKind.Sequential)]
	public struct SPEED
	{
	    public double vTcp;
	    public double vOri;
	    public double vLeax;
	    public double rReax;
	    public Int32 bStatic;
   }
   
  [StructLayout(LayoutKind.Sequential)]
	public struct VER_DATA
	{
	    public Int32 velType;   /* 0-VelPercent mode; 1-VelSpeed mode */
	    public Int32 velPercent;
	    public SPEED speed;
   }

  [StructLayout(LayoutKind.Sequential)]
  public struct MOV_IO
  {
      public Int32 IONo;
      public Int32 IOVa;
      public Int32 Kind;
      public Double Value;
  }

    public class IMC100API
    {
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Init_ETH")]
        public static extern Int32 IMC100_Init_ETH(UInt32 ipAddr, UInt16 ipPort, Int32 timeout, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Exit_ETH")]
        public static extern Int32 IMC100_Exit_ETH(Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_EmergStop")]
        public static extern Int32 IMC100_EmergStop(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MotorEnable")]
        public static extern Int32 IMC100_MotorEnable(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_ResetErr")]
        public static extern Int32 IMC100_ResetErr(Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_Mode")]
        public static extern Int32 IMC100_Set_Mode(Int32 mode, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_PrgCtrl")]
        public static extern Int32 IMC100_PrgCtrl(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_BackStartLine")]
        public static extern Int32 IMC100_BackStartLine(Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_Vel")]
        public static extern Int32 IMC100_Set_Vel(Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_AccRamp")]
        public static extern Int32 IMC100_Set_AccRamp(Double startVal, Double endVal, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RapidMove")]
        public static extern Int32 IMC100_Set_RapidMove(Int32 movType, Int32 enableFlag, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_SLVSMode")]
        public static extern Int32 IMC100_Set_SLVSMode(Int32 mode, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_FlyMode")]
        public static extern Int32 IMC100_Set_FlyMode(Int32 cpMode, Int32 flyMode, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_FlyPress")]
        public static extern Int32 IMC100_Set_FlyPress(Int32 flyPressPos, Int32 flyPressOrient, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_DsMode")]
        public static extern Int32 IMC100_DsMode(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_SlewMode")]
        public static extern Int32 IMC100_Set_SlewMode(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_DO")]
        public static extern Int32 IMC100_Set_DO(Int32 num, Int32 status, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_DOGroup")]
        public static extern Int32 IMC100_Set_DOGroup(Int32 num, Int32 status, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_DA")]
        public static extern Int32 IMC100_Set_DA(Int32 num, Single val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_InchMode")]
        public static extern Int32 IMC100_InchMode(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_InchStep")]
        public static extern Int32 IMC100_Set_InchStep(Int32 val, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_AxisJog")]
        public static extern Int32 IMC100_AxisJog(Int32 axis, Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_AxisInch")]
        public static extern Int32 IMC100_AxisInch(Int32 axis, Int32 cmd, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_PoseAlign")]
        public static extern Int32 IMC100_PoseAlign(Int32 coord, Int32 cmd, Int32 comId);
		
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ActiveMechUnit")]
        public static extern Int32 IMC100_Set_ActiveMechUnit(Byte[] mecUnit, Int32 comId);	
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ActiveMechUnit")]
        public static extern Int32 IMC100_Get_ActiveMechUnit(Byte[] mecUnit, Int32 comId);
		
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachCoordinate")]
        public static extern Int32 IMC100_Set_TeachCoordinate(Int32 flag, Int32 comId);	
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachCoordinate")]
        public static extern Int32 IMC100_Get_TeachCoordinate(ref Int32 flag, Int32 comId);
		
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Home")]
        public static extern Int32 IMC100_Home(Int32 num, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "Set_DynamicBrake")]
        public static extern Int32 IMC100_Set_DynamicBrake(Int32 flag, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "Get_DynamicBrake")]
        public static extern Int32 IMC100_Get_DynamicBrake(ref Int32 flag, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovJ_P")]
        public static extern Int32 IMC100_MovJ_P(Int32 posNum, Int32 vel, Int32 zone, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovL_P")]
        public static extern Int32 IMC100_MovL_P(Int32 posNum, Int32 vel, Int32 zone, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovC_P")]
        public static extern Int32 IMC100_MovC_P(Int32 posMidNum, Int32 posDstNum, Int32 vel, Int32 zone, Int32 comId);
      
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovJ_P_IO")]
        public static extern Int32 IMC100_MovJ_P_IO(Int32 posNum, Int32 vel, Int32 zone, MOV_IO[] movIo, Int32 ioNum, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovL_P_IO")]
        public static extern Int32 IMC100_MovL_P_IO(Int32 posNum, Int32 vel, Int32 zone, MOV_IO[] movIo, Int32 ioNum, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovC_P_IO")]
        public static extern Int32 IMC100_MovC_P_IO(Int32 posMidNum, Int32 posDstNum, Int32 vel, Int32 zone, MOV_IO[] movIo, Int32 ioNum, Int32 comId);
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Jump_P")]
        public static extern Int32 IMC100_Jump_P(Int32 posNum, Int32 vel, Int32 zone, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_JumpL_P")]
        public static extern Int32 IMC100_JumpL_P(Int32 posNum, Int32 vel, Int32 zone, Int32 comId);
       
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Jump_P_IO")]
        public static extern Int32 IMC100_Jump_P_IO(Int32 posNum, Int32 vel, Int32 zone, MOV_IO[] movIo, Int32 ioNum,Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_JumpL_P_IO")]
        public static extern Int32 IMC100_JumpL_P_IO(Int32 posNum, Int32 vel, Int32 zone, MOV_IO[] movIo, Int32 ioNum, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovJ_RobPos")]
        public static extern Int32 IMC100_MovJ_RobPos(ref ROB_POS pos, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovL_RobPos")]
        public static extern Int32 IMC100_MovL_RobPos(ref ROB_POS pos, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
	    [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovC_RobPos")]
        public static extern Int32 IMC100_MovC_RobPos(ref ROB_POS posMid, ref ROB_POS posDst, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
	    [DllImport("IMC100API.dll", EntryPoint = "IMC100_Jump_RobPos")]
        public static extern Int32 IMC100_Jump_RobPos(ref ROB_POS pos, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_JumpL_RobPos")]
        public static extern Int32 IMC100_JumpL_RobPos(ref ROB_POS pos, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
	  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovJAbs_RobJPos")]
        public static extern Int32 IMC100_MovJAbs_RobJPos(ref ROB_JPOS pos, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
       [DllImport("IMC100API.dll", EntryPoint = "IMC100_MovJAbs_JP")]
        public static extern Int32 IMC100_MovJAbs_JP(Int32 posNum, ref VER_DATA vel, Int32 zone, Int32 ioNum, ref MOV_IO movIo, Int32 comId);
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_IndCMove")]
        public static extern Int32 IMC100_IndCMove(Byte[] mecUnit, Int32 axis, Double speed, Double acc, Double dec, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_IndSpeed")]
        public static extern Int32 IMC100_IndSpeed(Byte[] mecUnit, Int32 axis, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_IndResetOld")]
        public static extern Int32 IMC100_IndResetOld(Byte[] mecUnit, Int32 axis, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_IndReset")]
        public static extern Int32 IMC100_IndReset(Byte[] mecUnit, Int32 axis, Double refNum, Int32 direction, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_IndCMoveSts")]
        public static extern Int32 IMC100_Get_IndCMoveSts(Byte[] mecUnit, Int32 axis, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_MotionStsExceptIndAxis")]
        public static extern Int32 IMC100_Get_MotionStsExceptIndAxis(ref Int32 sts, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobPosHere")]
        public static extern Int32 IMC100_Get_RobPosHere(ref ROB_POS pos, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobJPosHere")]
        public static extern Int32 IMC100_Get_RobJPosHere(ref ROB_JPOS pos, Int32 comId);			
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PosHerePulse")]
        public static extern Int32 IMC100_Get_PosHerePulse(Double[] pos, Int32 comId);    //pos.Length >= 6
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ActiveMechUnitPosFormat")]
        public static extern Int32 IMC100_Set_ActiveMechUnitPosFormat(Int32 posFormat, Int32 comId);	
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ActiveMechUnitPos")]
        public static extern Int32 IMC100_Get_ActiveMechUnitPos(ref Int32 posFormat, ref double mechUnitPos, Int32 comId);
			
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobJToRobP")]
        public static extern Int32 IMC100_Get_RobJToRobP(ref ROB_JPOS posSrc, Int32 toolnum, Int32 wobjnum, Int32 loadnum, ref ROB_POS posDst, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobPToRobJ")]
        public static extern Int32 IMC100_Get_RobPToRobJ(ref ROB_POS posSrc, Int32 toolnum, Int32 wobjnum, Int32 loadnum, ref ROB_JPOS posDst, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_FixWobjRobP")]
        public static extern Int32 IMC100_Get_FixWobjRobP(ref ROB_POS posSrc, Int32 wobjnum, ref ROB_POS posDst, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobHoldWobjRobP")]
        public static extern Int32 IMC100_Get_RobHoldWobjRobP(ref ROB_POS posBase, Int32 wobjnum, ref ROB_POS posRef, ref ROB_POS posDst, Int32 comId);
		
		
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_OffsetJ_RobJP")]
        public static extern Int32 IMC100_Get_OffsetJ_RobJP(ref ROB_JPOS posSrc, Double[] PR, ref ROB_JPOS posDst, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_Offset_RobP")]
        public static extern Int32 IMC100_Get_Offset_RobP(ref ROB_POS posSrc, Double[] PR, ref ROB_POS posDst, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_OffsetT_RobP")]
        public static extern Int32 IMC100_Get_OffsetT_RobP(ref ROB_POS posSrc, Double[] PR, ref ROB_POS posDst, Int32 comId);  
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_SysErrSts")]
        public static extern Int32 IMC100_Get_SysErrSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_SysErr")]
        public static extern Int32 IMC100_Get_SysErr(ref Int32 err, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TaskPrgPath")]
        public static extern Int32 IMC100_Get_TaskPrgPath(Int32 taskId, Byte[] prgPath, Int32 comId);    //prgPath.Length >= 128
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TaskRunSts")]
        public static extern Int32 IMC100_Get_TaskRunSts(Int32 taskId, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TaskProgramLine")]
        public static extern Int32 IMC100_Get_TaskProgramLine(Int32 taskId, ref Int32 line, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CurMotionLine")]
        public static extern Int32 IMC100_Get_CurMotionLine(ref Int32 line, Int32 comId);
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_InitSts")]
        public static extern Int32 IMC100_Get_InitSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CoordType")]
        public static extern Int32 IMC100_Get_CoordType(ref Int32 type, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_AccRamp")]
        public static extern Int32 IMC100_Get_AccRamp(ref double startVal, ref double endVal, Int32 comId);
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RapidMove")]
        public static extern Int32 IMC100_Get_RapidMove(Int32 movType, ref Int32 enableFlag, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_SLVSMode")]
        public static extern Int32 IMC100_Get_SLVSMode(ref Int32 enableFlag, Int32 comId);
        
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_FlyMode")]
        public static extern Int32 IMC100_Get_FlyMode(Int32 cpMode, ref Int32 flyMode, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_FlyPress")]
        public static extern Int32 IMC100_Get_FlyPress(ref Int32 flyPressPos, ref Int32 flyPressOrient, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_Vel")]
        public static extern Int32 IMC100_Get_Vel(ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_Mode")]
        public static extern Int32 IMC100_Get_Mode(ref Int32 mode, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DsMode")]
        public static extern Int32 IMC100_Get_DsMode(ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_InchMode")]
        public static extern Int32 IMC100_Get_InchMode(ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_SlewMode")]
        public static extern Int32 IMC100_Get_SlewMode(ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_EStopSts")]
        public static extern Int32 IMC100_Get_EStopSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_MotorSts")]
        public static extern Int32 IMC100_Get_MotorSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_MotionSts")]
        public static extern Int32 IMC100_Get_MotionSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_SysMode")]
        public static extern Int32 IMC100_Get_SysMode(ref Int32 mode, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PrgRunTime")]
        public static extern Int32 IMC100_Get_PrgRunTime(ref UInt32 second, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CurCmdNum")]
        public static extern Int32 IMC100_Get_CurCmdNum(ref UInt32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CurCmdSts")]
        public static extern Int32 IMC100_Get_CurCmdSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CmdSts")]
        public static extern Int32 IMC100_Get_CmdSts(Int32 val, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CurCmdCacheNum")]
        public static extern Int32 IMC100_Get_CurCmdCacheNum(ref Int32 num, Int32 comId);        

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DINum")]
        public static extern Int32 IMC100_Get_DINum(ref UInt32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DONum")]
        public static extern Int32 IMC100_Get_DONum(ref UInt32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ADNum")]
        public static extern Int32 IMC100_Get_ADNum(ref UInt32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DANum")]
        public static extern Int32 IMC100_Get_DANum(ref UInt32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DI")]
        public static extern Int32 IMC100_Get_DI(Int32 num, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DIGroup")]
        public static extern Int32 IMC100_Get_DIGroup(Int32 num, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_AD")]
        public static extern Int32 IMC100_Get_AD(Int32 num, ref Single val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DOCfg")]
        public static extern Int32 IMC100_Get_DOCfg(Int32 num, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DOGroupCfg")]
        public static extern Int32 IMC100_Get_DOGroupCfg(Int32 num, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DO")]
        public static extern Int32 IMC100_Get_DO(Int32 num, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DOGroup")]
        public static extern Int32 IMC100_Get_DOGroup(Int32 num, ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DACfg")]
        public static extern Int32 IMC100_Get_DACfg(Int32 num, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DA")]
        public static extern Int32 IMC100_Get_DA(Int32 num, ref Single val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_DevSts")]
        public static extern Int32 IMC100_Get_DevSts(Int32[] sts, Int32 comId);    //sts.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_FwVersion")]
        public static extern Int32 IMC100_Get_FwVersion(Byte[] ver, Int32 comId);    //ver.Length >= 32
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_SysTime")]
        public static extern Int32 IMC100_Get_SysTime(Byte[] time, Int32 comId);    //time.Length >= 16
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobotType")]
        public static extern Int32 IMC100_Get_RobotType(Byte[] type, Int32 comId);    //type.Length >= 128

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ArmType")]
        public static extern Int32 IMC100_Get_ArmType(Double[] pos, Int32[] armType, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ServoSts")]
        public static extern Int32 IMC100_Get_ServoSts(Int32[] sts, Int32 comId);    //sts.Length >= 8
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ServoErr")]
        public static extern Int32 IMC100_Get_ServoErr(Int32 num, ref Int32 err, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StrPara")]
        public static extern Int32 IMC100_Get_StrPara(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StrPara")]
        public static extern Int32 IMC100_Set_StrPara(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StrParaComp")]
        public static extern Int32 IMC100_Get_StrParaComp(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StrParaComp")]
        public static extern Int32 IMC100_Set_StrParaComp(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RdctRatio")]
        public static extern Int32 IMC100_Get_RdctRatio(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RdctRatio")]
        public static extern Int32 IMC100_Set_RdctRatio(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CpParaM")]
        public static extern Int32 IMC100_Get_CpParaM(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_CpParaM")]
        public static extern Int32 IMC100_Set_CpParaM(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CpParaS")]
        public static extern Int32 IMC100_Get_CpParaS(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_CpParaS")]
        public static extern Int32 IMC100_Set_CpParaS(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_HomeJPos")]
        public static extern Int32 IMC100_Get_HomeJPos(Int32 num, ref ROB_JPOS pos, Int32 comId);    
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_HomeJPos")]
        public static extern Int32 IMC100_Set_HomeJPos(Int32 num, ref ROB_JPOS pos, Int32 comId);    
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ZeroPos")]
        public static extern Int32 IMC100_Get_ZeroPos(Int32[] pluse, Int32 comId);    //pluse.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ZeroPos")]
        public static extern Int32 IMC100_Set_ZeroPos(Int32[] pluse, Int32 comId);    //pluse.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_InchStep")]
        public static extern Int32 IMC100_Get_InchStep(ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StepMotionJ")]
        public static extern Int32 IMC100_Get_StepMotionJ(ref Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StepMotionJ")]
        public static extern Int32 IMC100_Set_StepMotionJ(Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StepMotionL")]
        public static extern Int32 IMC100_Get_StepMotionL(ref Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StepMotionL")]
        public static extern Int32 IMC100_Set_StepMotionL(Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StepMotionR")]
        public static extern Int32 IMC100_Get_StepMotionR(ref Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StepMotionR")]
        public static extern Int32 IMC100_Set_StepMotionR(Single para, Int32 comId);
        
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachVelLimJ")]
        public static extern Int32 IMC100_Get_TeachVelLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachVelLimJ")]
        public static extern Int32 IMC100_Set_TeachVelLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachVelLimL")]
        public static extern Int32 IMC100_Get_TeachVelLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachVelLimL")]
        public static extern Int32 IMC100_Set_TeachVelLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachAccLimJ")]
        public static extern Int32 IMC100_Get_TeachAccLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachAccLimJ")]
        public static extern Int32 IMC100_Set_TeachAccLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachAccLimL")]
        public static extern Int32 IMC100_Get_TeachAccLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachAccLimL")]
        public static extern Int32 IMC100_Set_TeachAccLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RunVelLimJ")]
        public static extern Int32 IMC100_Get_RunVelLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RunVelLimJ")]
        public static extern Int32 IMC100_Set_RunVelLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RunVelLimL")]
        public static extern Int32 IMC100_Get_RunVelLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RunVelLimL")]
        public static extern Int32 IMC100_Set_RunVelLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RunAccLimJ")]
        public static extern Int32 IMC100_Get_RunAccLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RunAccLimJ")]
        public static extern Int32 IMC100_Set_RunAccLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RunAccLimL")]
        public static extern Int32 IMC100_Get_RunAccLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RunAccLimL")]
        public static extern Int32 IMC100_Set_RunAccLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StopDecLimJ")]
        public static extern Int32 IMC100_Get_StopDecLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StopDecLimJ")]
        public static extern Int32 IMC100_Set_StopDecLimJ(Single[] para, Int32 comId);    //para.Length >= 6
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_StopDecLimL")]
        public static extern Int32 IMC100_Get_StopDecLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_StopDecLimL")]
        public static extern Int32 IMC100_Set_StopDecLimL(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ZonePara")]
        public static extern Int32 IMC100_Get_ZonePara(Single[] para, Int32 comId);    //para.Length >= 2
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ZonePara")]
        public static extern Int32 IMC100_Set_ZonePara(Single[] para, Int32 comId);    //para.Length >= 2

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_AxisNLim")]
        public static extern Int32 IMC100_Get_AxisNLim(Int32 axis, ref Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_AxisNLim")]
        public static extern Int32 IMC100_Set_AxisNLim(Int32 axis, Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_AxisPLim")]
        public static extern Int32 IMC100_Get_AxisPLim(Int32 axis, ref Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_AxisPLim")]
        public static extern Int32 IMC100_Set_AxisPLim(Int32 axis, Single para, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ToolData")]
        public static extern Int32 IMC100_Get_ToolData(Int32 num, ref TOOL_DATA para, Int32 comId);    
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ToolData")]
        public static extern Int32 IMC100_Set_ToolData(Int32 num, ref TOOL_DATA para, Int32 comId);    
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_WobjData")]
        public static extern Int32 IMC100_Get_WobjData(Int32 num, ref WOBJ_DATA para, Int32 comId);      
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_WobjData")]
        public static extern Int32 IMC100_Set_WobjData(Int32 num, ref WOBJ_DATA para, Int32 comId);     
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ToolCNum")]
        public static extern Int32 IMC100_Get_ToolCNum(ref Int32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ToolCNum")]
        public static extern Int32 IMC100_Set_ToolCNum(Int32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_WobjNum")]
        public static extern Int32 IMC100_Get_WobjNum(ref Int32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_WobjNum")]
        public static extern Int32 IMC100_Set_WobjNum(Int32 num, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_CoordType")]
        public static extern Int32 IMC100_Set_CoordType(Int32 type, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_JumpPara")]
        public static extern Int32 IMC100_Get_JumpPara(ref Single lh, ref Single mh, ref Single rh, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_JumpPara")]
        public static extern Int32 IMC100_Set_JumpPara(Single lh, Single mh, Single rh, Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PalletPara")]
        public static extern Int32 IMC100_Get_PalletPara(ref Int32 rowNum, ref Int32 colNum, ref Int32 layerNum, ref Double layerHeight, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_PalletPara")]
        public static extern Int32 IMC100_Set_PalletPara(Int32 rowNum, Int32 colNum, Int32 layerNum, Double layerHeight, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Clear_PalletPara")]
        public static extern Int32 IMC100_Clear_PalletPara(Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_Pallet_RobP")]
        public static extern Int32 IMC100_Get_Pallet_RobP(ref ROB_POS pos1, ref ROB_POS pos2, ref ROB_POS pos3, Int32 rowIndex, Int32 colIndex, Int32 layIndex, ref ROB_POS posDst, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_Pallet4_RobP")]
        public static extern Int32 IMC100_Get_Pallet4_RobP(ref ROB_POS pos1, ref ROB_POS pos2, ref ROB_POS pos3, ref ROB_POS pos4, Int32 rowIndex, Int32 colIndex, Int32 layIndex, ref ROB_POS posDst, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_SavePara")]
        public static extern Int32 IMC100_SavePara(Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_RecoverPara")]
        public static extern Int32 IMC100_RecoverPara(Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobP")]
        public static extern Int32 IMC100_Get_RobP(Int32 pNum, ref ROB_POS pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RobP")]
        public static extern Int32 IMC100_Set_RobP(Int32 pNum, ref ROB_POS pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_MemRobP")]
        public static extern Int32 IMC100_Set_MemRobP(Int32 pNum, ref ROB_POS pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobPFromFile")]
		public static extern Int32 IMC100_Get_RobPFromFile(Byte[] plabelName, Int32 pNum, ref ROB_POS pos, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RobPToFile")]
		public static extern Int32 IMC100_Set_RobPToFile(Byte[] plabelName, Int32 pNum, ref ROB_POS pos, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CurRobPFileName")]
		public static extern Int32 IMC100_Get_CurRobPFileName(Byte[] plabelName, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RobPHere")]	
        public static extern Int32 IMC100_Set_RobPHere(Int32 pNum, Int32 comId);
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RobPHereToFile")]	
        public static extern Int32 IMC100_Set_RobPHereToFile(Byte[] plabelName, Int32 pNum, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobJP")]
        public static extern Int32 IMC100_Get_RobJP(Int32 pNum,  ref ROB_JPOS pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RobJP")]
        public static extern Int32 IMC100_Set_RobJP(Int32 pNum,  ref ROB_JPOS pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_MemRobJP")]
        public static extern Int32 IMC100_Set_MemRobJP(Int32 pNum, ref ROB_JPOS pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_RobJPHere")]
        public static extern Int32 IMC100_Set_RobJPHere(Int32 pNum,  Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobP_Label")]
        public static extern Int32 IMC100_Get_RobP_Label(Int32 pNum, Byte[] plabelName, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobJP_Label")]
        public static extern Int32 IMC100_Get_RobJP_Label(Int32 pNum, Byte[] plabelName, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobLP_Label")]
        public static extern Int32 IMC100_Get_RobLP_Label(Int32 taskId, Byte[] pProName, Int32 pNum, Byte[] plabelName, Int32 comId);
				
		
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PRVar")]
        public static extern Int32 IMC100_Get_PRVar(Int32 prNum, ref POSE pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_PRVar")]
        public static extern Int32 IMC100_Set_PRVar(Int32 prNum, POSE pos, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_B")]
        public static extern Int32 IMC100_Get_B(Int32 num, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_B")]
        public static extern Int32 IMC100_Set_B(Int32 num, Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_R")]
        public static extern Int32 IMC100_Get_R(Int32 num, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_R")]
        public static extern Int32 IMC100_Set_R(Int32 num, Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_D")]
        public static extern Int32 IMC100_Get_D(Int32 num, ref Double val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_D")]
        public static extern Int32 IMC100_Set_D(Int32 num, Double val, Int32 comId);        
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ModbusCoil")]
        public static extern Int32 IMC100_Get_ModbusCoil(Int32 address, Int32 sum, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ModbusCoil")]
        public static extern Int32 IMC100_Set_ModbusCoil(Int32 address, Int32 sum, Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ModbusRegUshort")]
        public static extern Int32 IMC100_Get_ModbusRegUshort(Int32 address, Int32 sum, UInt16[] val, Int32 comId);    //val.Length >= sum
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ModbusRegUshort")]
        public static extern Int32 IMC100_Set_ModbusRegUshort(Int32 address, Int32 sum, UInt16[] val, Int32 comId);    //val.Length >= sum
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_ModbusRegFloat")]
        public static extern Int32 IMC100_Get_ModbusRegFloat(Int32 address, Int32 sum, Single[] val, Int32 comId);    //val.Length >= sum
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_ModbusRegFloat")]
        public static extern Int32 IMC100_Set_ModbusRegFloat(Int32 address, Int32 sum, Single[] val, Int32 comId);    //val.Length >= sum
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlcVarByte")]
        public static extern Int32 IMC100_Get_PlcVarByte(Int32 num, ref Byte val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlcVarInt")]
        public static extern Int32 IMC100_Get_PlcVarInt(Int32 num, ref Int16 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlcVarDInt")]
        public static extern Int32 IMC100_Get_PlcVarDInt(Int32 num, ref Int32 val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlcVarLReal")]
        public static extern Int32 IMC100_Get_PlcVarLReal(Int32 num, ref Double val, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_UserAlarm")]
        public static extern Int32 IMC100_Get_UserAlarm(Int32 num, Byte[] alarm, Int32 comId);    //alarm.Length >= 40
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_UserAlarm")]
        public static extern Int32 IMC100_Set_UserAlarm(Int32 num, Byte[] alarm, Int32 comId);    //alarm.Length >= 40
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_Print")]
        public static extern Int32 IMC100_Get_Print(Byte[] val, Int32 comId);    //val.Length >= 128

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_CurCtrlDev")]
        public static extern Int32 IMC100_CurCtrlDev(ref Int32 dev, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_CurPermit")]
        public static extern Int32 IMC100_CurPermit(ref Int32 owner, ref UInt32 ipAddr, ref UInt16 ipPort, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_AcqPermit")]
        public static extern Int32 IMC100_AcqPermit(Int32 cmd, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_RemovePermit")]
        public static extern Int32 IMC100_RemovePermit(Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_CurUserType")]
        public static extern Int32 IMC100_CurUserType(ref Int32 type, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_UserLogin")]
        public static extern Int32 IMC100_UserLogin(Int32 tyte, Byte[] password, Int32 comId);    //password.Length >= 8
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_UserLogout")]
        public static extern Int32 IMC100_UserLogout(Int32 comId);

        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_SysTime")]
        public static extern Int32 IMC100_Set_SysTime(Byte[] time, Int32 comId);    //time.Length >= 16
				
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_LatchEnable")]
        public static extern Int32 IMC100_LatchEnable(Int32 cmd, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_LatchSts")]
        public static extern Int32 IMC100_Get_LatchSts(ref Int32 sts, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_LatchSum")]
        public static extern Int32 IMC100_Get_LatchSum(ref Int32 sum, Int32 comId);
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_LatchRobP")]
        public static extern Int32 IMC100_Get_LatchRobP(Int32 index, ref Int32 sts, ref ROB_POS pos, Int32 comId);  //pos[6]
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Clr_LatchPos")]
        public static extern Int32 IMC100_Clr_LatchPos(Int32 comId);
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_CollModeAndAction")]
        public static extern Int32 IMC100_Set_CollModeAndAction(Int32 checkflag, Int32 action, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_CollModeAndAction")]
        public static extern Int32 IMC100_Get_CollModeAndAction(ref Int32 checkflag, ref Int32 action, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_AxisCollMode")]
        public static extern Int32 IMC100_Set_AxisCollMode(Int32 axisNo, Int32 checkflag, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_AxisCollMode")]
        public static extern Int32 IMC100_Get_AxisCollMode(Int32 axisNo, ref Int32 checkflag, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_AxisCollLevel")]
        public static extern Int32 IMC100_Set_AxisCollLevel(Int32 axisNo, Single level, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_AxisCollLevel")]
        public static extern Int32 IMC100_Get_AxisCollLevel(Int32 axisNo, ref Single level, Int32 comId);  
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachModeAxisCollMode")]
        public static extern Int32 IMC100_Set_TeachModeAxisCollMode(Int32 axisNo, Int32 checkflag, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachModeAxisCollMode")]
        public static extern Int32 IMC100_Get_TeachModeAxisCollMode(Int32 axisNo, ref Int32 checkflag, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachModeCollAction")]
        public static extern Int32 IMC100_Set_TeachModeCollAction(Int32 action, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachModeCollAction")]
        public static extern Int32 IMC100_Get_TeachModeCollAction(ref Int32 action, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_TeachModeAxisCollLevel")]
        public static extern Int32 IMC100_Set_TeachModeAxisCollLevel(Int32 axisNo, Single level, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_TeachModeAxisCollLevel")]
        public static extern Int32 IMC100_Get_TeachModeAxisCollLevel(Int32 axisNo, ref Single level, Int32 comId);
        
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_PlayBackModeAxisCollMode")]
        public static extern Int32 IMC100_Set_PlayBackModeAxisCollMode(Int32 axisNo, Int32 checkflag, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlayBackModeAxisCollMode")]
        public static extern Int32 IMC100_Get_PlayBackModeAxisCollMode(Int32 axisNo, ref Int32 checkflag, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_PlayBackModeCollAction")]
        public static extern Int32 IMC100_Set_PlayBackModeCollAction(Int32 action, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlayBackModeCollAction")]
        public static extern Int32 IMC100_Get_PlayBackModeCollAction(ref Int32 action, Int32 comId);  
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Set_PlayBackModeAxisCollLevel")]
        public static extern Int32 IMC100_Set_PlayBackModeAxisCollLevel(Int32 axisNo, Single level, Int32 comId);   
        [DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_PlayBackModeAxisCollLevel")]
        public static extern Int32 IMC100_Get_PlayBackModeAxisCollLevel(Int32 axisNo, ref Single level, Int32 comId);
		
		[DllImport("IMC100API.dll", EntryPoint = "IMC100_Get_RobotAxisNum")]
        public static extern Int32 IMC100_Get_RobotAxisNum(ref Int32 axisNum, Int32 comId);
    }
}
