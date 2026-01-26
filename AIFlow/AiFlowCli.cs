using System;
using System.Collections.Generic;

namespace Automation.AIFlow
{
    public static class AiFlowCli
    {
        public static bool TryHandle(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return false;
            }
            if (!string.Equals(args[0], "aiflow", StringComparison.Ordinal))
            {
                return false;
            }

            int exitCode = Run(args);
            Environment.ExitCode = exitCode;
            return true;
        }

        private static int Run(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            string command = args[1];
            switch (command)
            {
                case "compile":
                    return RunCompile(args);
                case "verify":
                    return RunVerify(args);
                case "delta-apply":
                    return RunDeltaApply(args);
                case "diff":
                    return RunDiff(args);
                case "simulate":
                    return RunSimulate(args);
                case "rollback":
                    return RunRollback(args);
                case "collab-verify":
                    return RunCollabVerify(args);
                case "decompile":
                    return RunDecompile(args);
                default:
                    Console.Error.WriteLine($"未知命令:{command}");
                    PrintUsage();
                    return 2;
            }
        }

        private static int RunCompile(string[] args)
        {
            string corePath = null;
            string specPath = null;
            string outDir = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--core", StringComparison.Ordinal))
                {
                    if (corePath != null || specPath != null)
                    {
                        Console.Error.WriteLine("--core 与 --spec 只能二选一");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--core 缺少路径");
                        return 2;
                    }
                    corePath = args[++i];
                }
                else if (string.Equals(arg, "--spec", StringComparison.Ordinal))
                {
                    if (corePath != null || specPath != null)
                    {
                        Console.Error.WriteLine("--core 与 --spec 只能二选一");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--spec 缺少路径");
                        return 2;
                    }
                    specPath = args[++i];
                }
                else if (string.Equals(arg, "--out-dir", StringComparison.Ordinal))
                {
                    if (outDir != null)
                    {
                        Console.Error.WriteLine("--out-dir 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out-dir 缺少路径");
                        return 2;
                    }
                    outDir = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(corePath) && string.IsNullOrWhiteSpace(specPath))
            {
                Console.Error.WriteLine("必须指定 --core 或 --spec");
                return 2;
            }
            if (string.IsNullOrWhiteSpace(outDir))
            {
                Console.Error.WriteLine("必须指定 --out-dir");
                return 2;
            }

            string inputPath = corePath ?? specPath;
            AiFlowInputKind kind = corePath != null ? AiFlowInputKind.Core : AiFlowInputKind.Spec;

            AiFlowCompileResult result = AiFlowCompiler.CompileFromFile(inputPath, kind);
            if (!result.Success)
            {
                PrintIssues(result.Issues);
                return 1;
            }

            if (!AiFlowCompiler.ApplyToWorkPath(result.Procs, outDir, out List<AiFlowIssue> applyIssues))
            {
                PrintIssues(applyIssues);
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunVerify(string[] args)
        {
            string corePath = null;
            string specPath = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--core", StringComparison.Ordinal))
                {
                    if (corePath != null || specPath != null)
                    {
                        Console.Error.WriteLine("--core 与 --spec 只能二选一");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--core 缺少路径");
                        return 2;
                    }
                    corePath = args[++i];
                }
                else if (string.Equals(arg, "--spec", StringComparison.Ordinal))
                {
                    if (corePath != null || specPath != null)
                    {
                        Console.Error.WriteLine("--core 与 --spec 只能二选一");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--spec 缺少路径");
                        return 2;
                    }
                    specPath = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(corePath) && string.IsNullOrWhiteSpace(specPath))
            {
                Console.Error.WriteLine("必须指定 --core 或 --spec");
                return 2;
            }

            List<AiFlowIssue> issues;
            if (!string.IsNullOrWhiteSpace(corePath))
            {
                AiCoreFlow core = AiFlowIo.ReadCore(corePath, out issues);
                if (issues.Count > 0)
                {
                    PrintIssues(issues);
                    return 1;
                }
                issues = AiFlowVerifier.VerifyCore(core);
            }
            else
            {
                AiSpecFlow spec = AiFlowIo.ReadSpec(specPath, out issues);
                if (issues.Count > 0)
                {
                    PrintIssues(issues);
                    return 1;
                }
                issues = AiFlowVerifier.VerifySpec(spec);
            }

            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunDeltaApply(string[] args)
        {
            string baseCorePath = null;
            string deltaPath = null;
            string outCorePath = null;
            string outWorkDir = null;
            string diffPath = null;
            string revisionNote = null;
            bool saveRevision = false;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--base-core", StringComparison.Ordinal))
                {
                    if (baseCorePath != null)
                    {
                        Console.Error.WriteLine("--base-core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--base-core 缺少路径");
                        return 2;
                    }
                    baseCorePath = args[++i];
                }
                else if (string.Equals(arg, "--delta", StringComparison.Ordinal))
                {
                    if (deltaPath != null)
                    {
                        Console.Error.WriteLine("--delta 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--delta 缺少路径");
                        return 2;
                    }
                    deltaPath = args[++i];
                }
                else if (string.Equals(arg, "--out-core", StringComparison.Ordinal))
                {
                    if (outCorePath != null)
                    {
                        Console.Error.WriteLine("--out-core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out-core 缺少路径");
                        return 2;
                    }
                    outCorePath = args[++i];
                }
                else if (string.Equals(arg, "--out-work", StringComparison.Ordinal))
                {
                    if (outWorkDir != null)
                    {
                        Console.Error.WriteLine("--out-work 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out-work 缺少路径");
                        return 2;
                    }
                    outWorkDir = args[++i];
                }
                else if (string.Equals(arg, "--diff", StringComparison.Ordinal))
                {
                    if (diffPath != null)
                    {
                        Console.Error.WriteLine("--diff 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--diff 缺少路径");
                        return 2;
                    }
                    diffPath = args[++i];
                }
                else if (string.Equals(arg, "--save-revision", StringComparison.Ordinal))
                {
                    saveRevision = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        revisionNote = args[++i];
                    }
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(baseCorePath) || string.IsNullOrWhiteSpace(deltaPath) || string.IsNullOrWhiteSpace(outCorePath))
            {
                Console.Error.WriteLine("必须指定 --base-core --delta --out-core");
                return 2;
            }

            AiCoreFlow baseCore = AiFlowIo.ReadCore(baseCorePath, out List<AiFlowIssue> issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }
            AiFlowDelta delta = AiFlowIo.ReadDelta(deltaPath, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            AiCoreFlow nextCore = AiFlowDeltaApplier.Apply(baseCore, delta, out issues);
            if (issues.Count > 0 || nextCore == null)
            {
                PrintIssues(issues);
                return 1;
            }

            issues = AiFlowVerifier.VerifyCore(nextCore);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            AiFlowIo.WriteCore(outCorePath, nextCore, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(diffPath))
            {
                AiFlowDiffResult diff = AiFlowDiff.Build(baseCore, nextCore);
                AiFlowIo.WriteDiff(diffPath, diff, out issues);
                if (issues.Count > 0)
                {
                    PrintIssues(issues);
                    return 1;
                }
            }

            if (!string.IsNullOrWhiteSpace(outWorkDir))
            {
                if (saveRevision)
                {
                    if (!AiFlowRevision.SaveRevision(outWorkDir, revisionNote, out string revisionId, out issues))
                    {
                        PrintIssues(issues);
                        return 1;
                    }
                    Console.WriteLine($"REVISION:{revisionId}");
                }

                AiFlowCompileResult compile = AiFlowCompiler.CompileCore(nextCore);
                if (!compile.Success)
                {
                    PrintIssues(compile.Issues);
                    return 1;
                }
                if (!AiFlowCompiler.ApplyToWorkPath(compile.Procs, outWorkDir, out issues))
                {
                    PrintIssues(issues);
                    return 1;
                }
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunDiff(string[] args)
        {
            string baseCorePath = null;
            string targetCorePath = null;
            string outPath = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--base-core", StringComparison.Ordinal))
                {
                    if (baseCorePath != null)
                    {
                        Console.Error.WriteLine("--base-core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--base-core 缺少路径");
                        return 2;
                    }
                    baseCorePath = args[++i];
                }
                else if (string.Equals(arg, "--target-core", StringComparison.Ordinal))
                {
                    if (targetCorePath != null)
                    {
                        Console.Error.WriteLine("--target-core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--target-core 缺少路径");
                        return 2;
                    }
                    targetCorePath = args[++i];
                }
                else if (string.Equals(arg, "--out", StringComparison.Ordinal))
                {
                    if (outPath != null)
                    {
                        Console.Error.WriteLine("--out 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out 缺少路径");
                        return 2;
                    }
                    outPath = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(baseCorePath) || string.IsNullOrWhiteSpace(targetCorePath))
            {
                Console.Error.WriteLine("必须指定 --base-core --target-core");
                return 2;
            }

            AiCoreFlow baseCore = AiFlowIo.ReadCore(baseCorePath, out List<AiFlowIssue> issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }
            AiCoreFlow targetCore = AiFlowIo.ReadCore(targetCorePath, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            AiFlowDiffResult diff = AiFlowDiff.Build(baseCore, targetCore);
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                AiFlowIo.WriteDiff(outPath, diff, out issues);
                if (issues.Count > 0)
                {
                    PrintIssues(issues);
                    return 1;
                }
            }
            else
            {
                AiFlowIo.PrintDiff(diff);
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunRollback(string[] args)
        {
            string workDir = null;
            string revisionId = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--work-dir", StringComparison.Ordinal))
                {
                    if (workDir != null)
                    {
                        Console.Error.WriteLine("--work-dir 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--work-dir 缺少路径");
                        return 2;
                    }
                    workDir = args[++i];
                }
                else if (string.Equals(arg, "--revision", StringComparison.Ordinal))
                {
                    if (revisionId != null)
                    {
                        Console.Error.WriteLine("--revision 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--revision 缺少值");
                        return 2;
                    }
                    revisionId = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(workDir) || string.IsNullOrWhiteSpace(revisionId))
            {
                Console.Error.WriteLine("必须指定 --work-dir --revision");
                return 2;
            }

            if (!AiFlowRevision.Rollback(workDir, revisionId, out List<AiFlowIssue> issues))
            {
                PrintIssues(issues);
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunSimulate(string[] args)
        {
            string corePath = null;
            string scenarioPath = null;
            string outTracePath = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--core", StringComparison.Ordinal))
                {
                    if (corePath != null)
                    {
                        Console.Error.WriteLine("--core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--core 缺少路径");
                        return 2;
                    }
                    corePath = args[++i];
                }
                else if (string.Equals(arg, "--scenario", StringComparison.Ordinal))
                {
                    if (scenarioPath != null)
                    {
                        Console.Error.WriteLine("--scenario 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--scenario 缺少路径");
                        return 2;
                    }
                    scenarioPath = args[++i];
                }
                else if (string.Equals(arg, "--out-trace", StringComparison.Ordinal))
                {
                    if (outTracePath != null)
                    {
                        Console.Error.WriteLine("--out-trace 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out-trace 缺少路径");
                        return 2;
                    }
                    outTracePath = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(corePath) || string.IsNullOrWhiteSpace(scenarioPath) || string.IsNullOrWhiteSpace(outTracePath))
            {
                Console.Error.WriteLine("必须指定 --core --scenario --out-trace");
                return 2;
            }

            AiCoreFlow core = AiFlowIo.ReadCore(corePath, out List<AiFlowIssue> issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }
            AiFlowScenario scenario = AiFlowIo.ReadScenario(scenarioPath, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            AiFlowTrace trace = AiFlowSimulator.Simulate(core, scenario);
            AiFlowIo.WriteTrace(outTracePath, trace, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            if (trace.Issues.Count > 0)
            {
                PrintIssues(trace.Issues);
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunCollabVerify(string[] args)
        {
            string corePath = null;
            string contractPath = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--core", StringComparison.Ordinal))
                {
                    if (corePath != null)
                    {
                        Console.Error.WriteLine("--core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--core 缺少路径");
                        return 2;
                    }
                    corePath = args[++i];
                }
                else if (string.Equals(arg, "--contracts", StringComparison.Ordinal))
                {
                    if (contractPath != null)
                    {
                        Console.Error.WriteLine("--contracts 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--contracts 缺少路径");
                        return 2;
                    }
                    contractPath = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(corePath) || string.IsNullOrWhiteSpace(contractPath))
            {
                Console.Error.WriteLine("必须指定 --core --contracts");
                return 2;
            }

            AiCoreFlow core = AiFlowIo.ReadCore(corePath, out List<AiFlowIssue> issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }
            AiFlowContractSet contracts = AiFlowIo.ReadContracts(contractPath, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            issues = AiFlowCollaborationAnalyzer.Analyze(core, contracts);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static int RunDecompile(string[] args)
        {
            string workDir = null;
            string outCore = null;
            string outSpec = null;

            for (int i = 2; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--work-dir", StringComparison.Ordinal))
                {
                    if (workDir != null)
                    {
                        Console.Error.WriteLine("--work-dir 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--work-dir 缺少路径");
                        return 2;
                    }
                    workDir = args[++i];
                }
                else if (string.Equals(arg, "--out-core", StringComparison.Ordinal))
                {
                    if (outCore != null)
                    {
                        Console.Error.WriteLine("--out-core 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out-core 缺少路径");
                        return 2;
                    }
                    outCore = args[++i];
                }
                else if (string.Equals(arg, "--out-spec", StringComparison.Ordinal))
                {
                    if (outSpec != null)
                    {
                        Console.Error.WriteLine("--out-spec 重复");
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out-spec 缺少路径");
                        return 2;
                    }
                    outSpec = args[++i];
                }
                else if (string.Equals(arg, "--help", StringComparison.Ordinal))
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"未知参数:{arg}");
                    return 2;
                }
            }

            if (string.IsNullOrWhiteSpace(workDir) || string.IsNullOrWhiteSpace(outCore))
            {
                Console.Error.WriteLine("必须指定 --work-dir --out-core");
                return 2;
            }

            AiCoreFlow core = AiFlowDecompiler.DecompileWork(workDir, out List<AiFlowIssue> issues);
            if (issues.Count > 0 || core == null)
            {
                PrintIssues(issues);
                return 1;
            }
            AiFlowIo.WriteCore(outCore, core, out issues);
            if (issues.Count > 0)
            {
                PrintIssues(issues);
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(outSpec))
            {
                AiSpecFlow spec = AiFlowDecompiler.BuildSpec(core);
                AiFlowIo.WriteSpec(outSpec, spec, out issues);
                if (issues.Count > 0)
                {
                    PrintIssues(issues);
                    return 1;
                }
            }

            Console.WriteLine("OK");
            return 0;
        }

        private static void PrintIssues(List<AiFlowIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return;
            }
            foreach (AiFlowIssue issue in issues)
            {
                Console.Error.WriteLine($"[{issue.Code}] {issue.Location} {issue.Message}");
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  Automation.exe aiflow compile --core <core.json> --out-dir <Work目录>");
            Console.WriteLine("  Automation.exe aiflow compile --spec <spec.json> --out-dir <Work目录>");
            Console.WriteLine("  Automation.exe aiflow verify --core <core.json>");
            Console.WriteLine("  Automation.exe aiflow verify --spec <spec.json>");
            Console.WriteLine("  Automation.exe aiflow delta-apply --base-core <core.json> --delta <delta.json> --out-core <core.json> [--diff <diff.json>] [--out-work <Work目录>] [--save-revision [note]]");
            Console.WriteLine("  Automation.exe aiflow diff --base-core <core.json> --target-core <core.json> [--out <diff.json>]");
            Console.WriteLine("  Automation.exe aiflow simulate --core <core.json> --scenario <scenario.json> --out-trace <trace.json>");
            Console.WriteLine("  Automation.exe aiflow collab-verify --core <core.json> --contracts <contracts.json>");
            Console.WriteLine("  Automation.exe aiflow decompile --work-dir <Work目录> --out-core <core.json> [--out-spec <spec.json>]");
            Console.WriteLine("  Automation.exe aiflow rollback --work-dir <Work目录> --revision <id>");
            Console.WriteLine("参数:");
            Console.WriteLine("  --core    指定 core.json (core-1)");
            Console.WriteLine("  --spec    指定 spec.json (spec-1, kind=core)");
            Console.WriteLine("  --out-dir 输出 Work 目录路径");
        }
    }
}
