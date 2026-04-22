using LanguageCore.Parser.Statements;
using LanguageCore.Tokenizing;

namespace LanguageCore.Parser;

public class TemplateInfo : IPositioned
{
    public TokenPair Brackets { get; }
    public ImmutableArray<Token> Parameters { get; }

    public Position Position =>
        new Position(Parameters.As<IPositioned>().DefaultIfEmpty(Brackets))
        .Union(Brackets);

    public TemplateInfo(TokenPair brackets, ImmutableArray<Token> typeParameters)
    {
        Brackets = brackets;
        Parameters = typeParameters;
    }

    public bool TryGetTypeArgumentIndex(string typeArgumentName, out int index)
    {
        index = -1;
        for (int i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i].Content == typeArgumentName)
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    public bool OrderTypeArguments<TValue>(ImmutableDictionary<string, TValue> typeArguments, [NotNullWhen(true)] out ImmutableArray<TValue> result)
    {
        TValue[] _result = new TValue[Parameters.Length];

        foreach (KeyValuePair<string, TValue> item in typeArguments)
        {
            if (!TryGetTypeArgumentIndex(item.Key, out int i))
            {
                result = default;
                return false;
            }

            _result[i] = item.Value;
        }

        result = _result.AsImmutableUnsafe();
        return true;
    }

    public override string ToString() => $"{Brackets.Start}{string.Join(", ", Parameters)}{Brackets.End}";
}
