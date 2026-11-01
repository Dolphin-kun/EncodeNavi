namespace EncodeNavi.Recommendation
{
    internal record RecommendationItem(string Name, string Value, string Current, string Reason, string? ChangeHint = null, bool IsHandledByCommand = false)
    {
        public string? Target { get; init; }

        public bool IsChangeNeeded => Current != CurrentOutputSettings.Unknown && Current != Value;

        public bool IsMisconfigured => IsChangeNeeded && !IsHandledByCommand;

        public string ChangeText => IsChangeNeeded ? ChangeHint ?? "（要変更）" : "";
    }

    internal record EncodePlan(
        IReadOnlyList<RecommendationItem> Items,
        string VideoOptions,
        string AudioOptions,
        IReadOnlyList<string> Notes);
}
