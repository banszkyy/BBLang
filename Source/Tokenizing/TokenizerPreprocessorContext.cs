namespace LanguageCore.Tokenizing;

class TokenizerPreprocessorContext
{
    readonly DiagnosticsCollection Diagnostics;
    readonly Stack<PreprocessConditionItem> PreprocessConditionStack = new();
    readonly HashSet<string> PreprocessorVariables;
    readonly Uri File;

    public bool IsPreprocessSkipping => PreprocessConditionStack.Any(v => !v.PreviousConditions[^1]);

    enum PreprocessConditionPhase
    {
        If,
        Else,
    }

    class PreprocessConditionItem
    {
        public PreprocessConditionPhase Phase;
        public List<bool> PreviousConditions;

        public PreprocessConditionItem(PreprocessConditionPhase phase)
        {
            Phase = phase;
            PreviousConditions = new List<bool>();
        }
    }

    public TokenizerPreprocessorContext(DiagnosticsCollection diagnostics, IEnumerable<string> variables, Uri file)
    {
        Diagnostics = diagnostics;
        PreprocessorVariables = new HashSet<string>(variables);
        File = file;
    }

    public void HandlePreprocess(Token name, Token? argument)
    {
        switch (name.Content)
        {
            case "#if":
            {
                if (argument is null)
                { Diagnostics.Add(DiagnosticAt.Error($"Argument expected after preprocessor tag `{name}`", name.Position.After(), File)); }

                PreprocessConditionItem v = PreprocessConditionStack.Push(new PreprocessConditionItem(PreprocessConditionPhase.If));
                v.PreviousConditions.Add(PreprocessorVariables.Contains(argument?.Content.TrimStart() ?? string.Empty));

                break;
            }

            case "#elseif":
            {
                if (PreprocessConditionStack.Count == 0)
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Unexpected preprocessor tag `{name}`", name.Position, File));
                    break;
                }

                if (PreprocessConditionStack.Last.Phase == PreprocessConditionPhase.Else)
                { Diagnostics.Add(DiagnosticAt.Error($"Unexpected preprocessor tag `{name}`", name.Position, File)); }

                PreprocessConditionStack.Last.Phase = PreprocessConditionPhase.Else;
                PreprocessConditionStack.Last.PreviousConditions.Add(PreprocessConditionStack.Last.PreviousConditions.All(v => !v) && PreprocessorVariables.Contains(argument?.Content.TrimStart() ?? string.Empty));

                break;
            }

            case "#else":
            {
                if (PreprocessConditionStack.Count == 0)
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Unexpected preprocessor tag `{name}`", name.Position, File));
                    break;
                }

                if (PreprocessConditionStack.Last.Phase == PreprocessConditionPhase.Else)
                { Diagnostics.Add(DiagnosticAt.Error($"Unexpected preprocessor tag `{name}`", name.Position, File)); }

                PreprocessConditionStack.Last.Phase = PreprocessConditionPhase.Else;
                PreprocessConditionStack.Last.PreviousConditions.Add(PreprocessConditionStack.Last.PreviousConditions.All(v => !v));

                break;
            }

            case "#endif":
            {
                if (PreprocessConditionStack.Count == 0)
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Unexpected preprocessor tag `{name}`", name.Position, File));
                    break;
                }

                PreprocessConditionStack.Pop();

                break;
            }

            case "#define":
            {
                if (IsPreprocessSkipping)
                { break; }

                if (argument is null)
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Argument expected after preprocessor tag `{name}`", name.Position.After(), File));
                    break;
                }

                PreprocessorVariables.Add(argument.Content.TrimStart());

                break;
            }

            case "#undefine":
            {
                if (IsPreprocessSkipping)
                { break; }

                if (argument is null)
                {
                    Diagnostics.Add(DiagnosticAt.Error($"Argument expected after preprocessor tag `{name}`", name.Position.After(), File));
                    break;
                }

                PreprocessorVariables.Remove(argument.Content.TrimStart());

                break;
            }

            default:
            {
                Diagnostics.Add(DiagnosticAt.Error($"Unknown preprocessor tag `{name}`", name.Position.After(), File));
                break;
            }
        }
    }
}
