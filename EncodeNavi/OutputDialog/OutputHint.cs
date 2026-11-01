using EncodeNavi.Analysis;
using EncodeNavi.Recommendation;
using EncodeNavi.Settings;

namespace EncodeNavi.OutputDialog
{
    internal enum OutputHintKind
    {
        Warning,
        Ok,
        Info,
    }

    internal record OutputHint(string Target, OutputHintKind Kind, string Text, string? ToolTip = null, string? CopyText = null, bool ShowsDetailsButton = false);

    internal static class OutputHintBuilder
    {
        public static IReadOnlyList<OutputHint> Build()
        {
            if (OutputMethodDetector.GetSelectedOrNull() is not { } method)
                return [];

            var settings = EncodeNaviSettings.Default;
            if (AnalysisStore.Result is not { } analysis || AnalysisStore.Project is not { } project)
            {
                var text = AnalysisStore.IsAnalyzing
                    ? $"EncodeNavi: {AnalysisStore.StatusText}"
                    : "EncodeNavi: 「詳細」から解析すると、ここに推奨設定を表示します";
                return [new(OutputSettingTargets.General, OutputHintKind.Info, text, ShowsDetailsButton: true)];
            }

            var plan = Recommender.Recommend(settings.Goal, settings.Platform, method, project, analysis, CurrentOutputSettings.Read(method));
            var misconfigured = plan.Items.Where(x => x.IsMisconfigured && x.Target != null).ToList();
            var goalName = EnumText.Get(settings.Goal);
            var analyzingText = AnalysisStore.IsAnalyzing ? "（再解析中…）" : "";

            if (misconfigured.Count == 0)
                return [new(OutputSettingTargets.General, OutputHintKind.Ok, $"EncodeNavi: 推奨どおりです（{goalName}）{analyzingText}", ShowsDetailsButton: true)];

            var hints = new List<OutputHint>();
            foreach (var group in misconfigured.GroupBy(x => x.Target!))
            {
                var toolTip = string.Join("\n\n", group.Select(x => $"{x.Name}: {x.Current} → {x.Value}\n{x.Reason}"));
                var hint = group.Key switch
                {
                    OutputSettingTargets.VideoOptions => new OutputHint(group.Key, OutputHintKind.Warning, "推奨の映像コマンドに変更", toolTip, plan.VideoOptions),
                    OutputSettingTargets.AudioOptions => new OutputHint(group.Key, OutputHintKind.Warning, "推奨の音声コマンドに変更", toolTip, plan.AudioOptions),
                    OutputSettingTargets.General => null,
                    _ => new OutputHint(group.Key, OutputHintKind.Warning, "推奨: " + string.Join(" / ", group.Select(x => x.Value)), toolTip),
                };
                if (hint != null)
                    hints.Add(hint);
            }

            var general = misconfigured.Where(x => x.Target == OutputSettingTargets.General).ToList();
            var generalText = $"EncodeNavi: 推奨と違う設定が {misconfigured.Count} 件（{goalName}）{analyzingText}"
                + string.Concat(general.Select(x => $" / {x.Name}: {x.Value}{x.ChangeHint}"));
            var generalToolTip = string.Join("\n", misconfigured.Select(x => $"・{x.Name}: {x.Current} → {x.Value}"));
            hints.Add(new(OutputSettingTargets.General, OutputHintKind.Warning, generalText, generalToolTip, ShowsDetailsButton: true));

            return hints;
        }
    }
}
