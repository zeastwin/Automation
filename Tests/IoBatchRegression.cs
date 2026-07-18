using System;
using System.Collections.Generic;
using System.ComponentModel;
using Automation;
using Automation.MotionControl;
using Automation.Protocol;

internal static class IoBatchRegression
{
    private sealed class BatchIoProbe : IIoRuntime
    {
        public int SingleCalls { get; private set; }
        public int BatchCalls { get; private set; }
        public IReadOnlyList<IoOutputCommand> LastCommands { get; private set; }

        public bool SetIO(IO io, bool isOpen)
        {
            SingleCalls++;
            return true;
        }

        public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
        {
            BatchCalls++;
            LastCommands = commands;
            return true;
        }

        public bool GetOutIO(IO io, ref bool value) { return true; }
        public bool GetInIO(IO io, ref bool value) { return true; }
    }

    private static int Main()
    {
        PropertyDescriptor ioLogicName = TypeDescriptor
            .GetProperties(typeof(IoLogicGotoParam))[nameof(IoLogicGotoParam.IoName)];
        if (ioLogicName?.Converter?.GetType()
            != typeof(OperationTypePartial.IoInItem))
        {
            return 10;
        }

        var probe = new BatchIoProbe();
        var io0 = CreateOutput("Out0", 0, 0);
        var io1 = CreateOutput("Out1", 0, 1);
        var ioMap = new Dictionary<string, IO>(StringComparer.Ordinal);
        ioMap.Add(io0.Name, io0);
        ioMap.Add(io1.Name, io1);
        var context = new EngineContext
        {
            Io = probe,
            IoMap = ioMap
        };
        var engine = new ProcessEngine(context);
        var operation = new IoOperate
        {
            IoParams = new OperationTypePartial.CustomList<IoOutParam>
            {
                new IoOutParam { IoName = io0.Name, TargetState = true },
                new IoOutParam { IoName = io1.Name, TargetState = false }
            }
        };

        if (!engine.RunIoOperate(new ProcHandle(), operation)
            || probe.BatchCalls != 1 || probe.SingleCalls != 0
            || probe.LastCommands == null || probe.LastCommands.Count != 2)
        {
            return 1;
        }

        var resources = new Dictionary<string, AiIoResource>(StringComparer.Ordinal);
        resources.Add(io0.Name, new AiIoResource { IoType = io0.IOType, CardNum = 0, IoIndex = "0" });
        resources.Add(io1.Name, new AiIoResource { IoType = io1.IOType, CardNum = 0, IoIndex = "1" });
        resources.Add("In0", new AiIoResource { IoType = "通用输入", CardNum = 0, IoIndex = "2" });
        var compileContext = new AiOperationCompileContext(
            0, Guid.NewGuid(), new Dictionary<string, DicValue>(StringComparer.Ordinal),
            new AiResourceSnapshot(resources));
        var semantic = new SemanticOperation
        {
            Kind = "io.write",
            Outputs = new List<IoOutputState>
            {
                new IoOutputState { Io = io0.Name, State = true },
                new IoOutputState { Io = io1.Name, State = false }
            }
        };
        var compiled = AiOperationCompilerRegistry.Get("io.write").Compile(semantic, compileContext) as IoOperate;
        if (compiled == null || compiled.IoParams.Count != 2)
        {
            return 4;
        }

        var outputBranch = new SemanticOperation
        {
            Kind = "branch.io",
            Conditions = new List<IoStateCondition>
            {
                new IoStateCondition { Io = io0.Name, State = true }
            }
        };
        try
        {
            AiOperationCompilerRegistry.Get("branch.io").Compile(outputBranch, compileContext);
            return 11;
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains("只能引用通用输入"))
            {
                return 12;
            }
        }

        var inputWait = new SemanticOperation
        {
            Kind = "io.wait",
            TimeoutMs = 1000,
            Conditions = new List<IoStateCondition>
            {
                new IoStateCondition { Io = "In0", State = true }
            }
        };
        if (!(AiOperationCompilerRegistry.Get("io.wait")
            .Compile(inputWait, compileContext) is IoCheck))
        {
            return 13;
        }

        io1.CardNum = 1;
        try
        {
            engine.RunIoOperate(new ProcHandle(), operation);
            return 2;
        }
        catch (InvalidOperationException)
        {
            if (probe.BatchCalls != 1)
            {
                return 3;
            }
        }

        resources[io1.Name].CardNum = 1;
        try
        {
            AiOperationCompilerRegistry.Get("io.write").Compile(semantic, compileContext);
            return 5;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static IO CreateOutput(string name, int cardNum, int ioIndex)
    {
        return new IO
        {
            Name = name,
            CardNum = cardNum,
            IOIndex = ioIndex.ToString(),
            IOType = "通用输出"
        };
    }
}
