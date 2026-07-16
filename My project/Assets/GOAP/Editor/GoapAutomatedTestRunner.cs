using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;
using ApiTestMode = UnityEditor.TestTools.TestRunner.Api.TestMode;

namespace Practice.GOAP.Editor
{
    public static class GoapAutomatedTestRunner
    {
        private const string ActiveSessionKey = "Practice.GOAP.TestRunActive";
        private const string PhaseSessionKey = "Practice.GOAP.TestRunPhase";
        private const string MarkerFileName = "RunGoapTests.marker";
        private const string ResultFileName = "GOAPTestResults.txt";
        private const string RunTestsMenuPath = "Tools/GOAP/Run Automated Tests %#t";
        private const string EditModePhase = "EditMode";
        private const string PlayModePhase = "PlayMode";

        private static string TempDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp"));
        private static string MarkerPath => Path.Combine(TempDirectory, MarkerFileName);
        private static string ResultPath => Path.Combine(TempDirectory, ResultFileName);

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (SessionState.GetBool(ActiveSessionKey, false))
            {
                TestRunnerApi.RegisterTestCallback(new ResultCallbacks());
            }

            if (!File.Exists(MarkerPath))
            {
                return;
            }

            File.Delete(MarkerPath);
            EditorApplication.delayCall += RunAllTests;
        }

        [MenuItem(RunTestsMenuPath)]
        public static void RunAllTests()
        {
            Directory.CreateDirectory(TempDirectory);
            if (File.Exists(ResultPath))
            {
                File.Delete(ResultPath);
            }

            SessionState.SetBool(ActiveSessionKey, true);
            SessionState.SetString(PhaseSessionKey, EditModePhase);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ResultCallbacks());
            ExecutePhase(api, EditModePhase);
            Debug.Log("GOAP automated tests started.");
        }

        private static void ExecutePhase(TestRunnerApi api, string phase)
        {
            var playMode = phase == PlayModePhase;
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = playMode ? ApiTestMode.PlayMode : ApiTestMode.EditMode,
                assemblyNames = new[]
                {
                    playMode ? "Practice.GOAP.Tests.PlayMode" : "Practice.GOAP.Tests.EditMode"
                }
            }));
        }

        private sealed class ResultCallbacks : ICallbacks
        {
            private readonly StringBuilder _failures = new();

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var phase = SessionState.GetString(PhaseSessionKey, EditModePhase);
                var report = new StringBuilder()
                    .AppendLine($"[{phase}]")
                    .AppendLine($"Status: {result.TestStatus}")
                    .AppendLine($"Passed: {result.PassCount}")
                    .AppendLine($"Failed: {result.FailCount}")
                    .AppendLine($"Skipped: {result.SkipCount}")
                    .AppendLine($"Inconclusive: {result.InconclusiveCount}")
                    .AppendLine($"Duration: {result.Duration:0.000}s");

                if (_failures.Length > 0)
                {
                    report.AppendLine().AppendLine("Failures:").Append(_failures);
                }

                File.AppendAllText(ResultPath, report.AppendLine().ToString());

                if (phase == EditModePhase)
                {
                    SessionState.SetString(PhaseSessionKey, PlayModePhase);
                    EditorApplication.delayCall += () =>
                    {
                        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                        ExecutePhase(api, PlayModePhase);
                    };
                    Debug.Log($"GOAP Edit Mode tests finished: {result.TestStatus}. Starting Play Mode tests.");
                    return;
                }

                SessionState.SetBool(ActiveSessionKey, false);
                SessionState.EraseString(PhaseSessionKey);
                Debug.Log($"GOAP Play Mode tests finished: {result.TestStatus}. Report: {ResultPath}");
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.Test.HasChildren && result.TestStatus == TestStatus.Failed)
                {
                    _failures
                        .AppendLine(result.FullName)
                        .AppendLine(result.Message)
                        .AppendLine(result.StackTrace)
                        .AppendLine();
                }
            }
        }
    }
}
