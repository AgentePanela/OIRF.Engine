; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EA001 | Engine.Generators | Error | No matching constructor for [StyleField] default
EA002 | Engine.Generators | Error | Unsupported type on a networked message property - not a primitive/collection and not marked [Serializable].
EA003 | Engine.Generators | Warning | The message should be a partial class.