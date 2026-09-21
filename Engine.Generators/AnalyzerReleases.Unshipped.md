; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EA001 | Engine.Generators | Error | No matching constructor for [StyleField] default
EA002 | Engine.Generators | Error | Unsupported type on a networked message property - not a primitive/collection and not marked [Serializable].
EA003 | Engine.Generators | Warning | The message should be a partial class.
EA004 | Engine.Generators | Error | Invalid [NetworkedComponent] - must be a top-level, non-generic partial class deriving directly from Component.
EA005 | Engine.Generators | Error | Invalid [NetworkedField] - must be a property with a getter and a (non init-only) setter, or a non readonly/const field.