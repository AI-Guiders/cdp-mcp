#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    sealed class ComposerState
    {
        public bool HasInput { get; set; }
        public string? InputText { get; set; }
        public string? SubmitAria { get; set; }
        public bool? SubmitDisabled { get; set; }
        public bool ProviderBlocked { get; set; }
        public string? ProviderBlockedSource { get; set; }
        public string? ProviderBlockedText { get; set; }
        public string? StrayInputText { get; set; }
        public bool ComposerScoped { get; set; }
    }

    sealed class ProviderBlockedProbe
    {
        public bool Blocked { get; set; }
        public string? Source { get; set; }
        public string? Text { get; set; }
    }

    sealed class InsertResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Text { get; set; }
        public int Len { get; set; }
        public bool ComposerScoped { get; set; }
        public ProviderBlockedProbe? Blocked { get; set; }
        public object? Stray { get; set; }
    }

    sealed class ClickResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Kind { get; set; }
        public string? Aria { get; set; }
        public string? AriaBefore { get; set; }
        public string? AriaAfter { get; set; }
    }

    sealed class FocusChatResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? Text { get; set; }
    }

}
