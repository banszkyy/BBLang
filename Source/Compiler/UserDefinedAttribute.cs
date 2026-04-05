using LanguageCore.Parser;

namespace LanguageCore.Compiler;

public delegate bool AttributeVerifier(IHaveAttributes context, AttributeUsage attribute, [NotNullWhen(false)] out PossibleDiagnostic? error);

public delegate bool AttributeVerifier<T>(T context, AttributeUsage attribute, [NotNullWhen(false)] out PossibleDiagnostic? error);

public class UserDefinedAttribute
{
    public string Name { get; }
    public ImmutableArray<LiteralType> Parameters { get; }
    public CanUseOn CanUseOn { get; }
    public AttributeVerifier? Verifier { get; }

    UserDefinedAttribute(string name, ImmutableArray<LiteralType> parameters, CanUseOn canUseOn, AttributeVerifier? verifier)
    {
        Name = name;
        Parameters = parameters;
        CanUseOn = canUseOn;
        Verifier = verifier;
    }

    public static UserDefinedAttribute Create(string name, ImmutableArray<LiteralType> parameters, CanUseOn canUseOn, AttributeVerifier? verifier = null)
        => new(name, parameters, canUseOn, verifier);

    public static UserDefinedAttribute Create<T>(string name, ImmutableArray<LiteralType> parameters, CanUseOn canUseOn, AttributeVerifier<T>? verifier = null)
        where T : IHaveAttributes
        => new(name, parameters, canUseOn, bool (IHaveAttributes context, AttributeUsage attribute, [NotNullWhen(false)] out PossibleDiagnostic? error) =>
        {
            if (context is not T v)
            {
                error = new PossibleDiagnostic($"This attribute can only be used on '{typeof(T)}'");
                return false;
            }

            if (verifier is null)
            {
                error = null;
                return true;
            }

            return verifier(v, attribute, out error);
        });
}
