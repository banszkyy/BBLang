using System.IO;

namespace LanguageCore;

public static partial class Stringifier
{
    public abstract class BuilderBase
    {
        public int IndentLevel;
        public int IndentSize = 4;
        public bool Minimize;

        public abstract void Append(char v);
        public abstract void Append(string v);
        public virtual void Append(char v, int repeatCount)
        {
            for (int i = 0; i < repeatCount; i++) Append(v);
        }
        public virtual void AppendJoin<T>(ImmutableArray<T> items, Action<T> callback, Action? join = null)
        {
            join ??= () => { Append(','); Space(); };
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0)
                {
                    join.Invoke();
                }
                callback.Invoke(items[i]);
            }
        }
        public virtual void AppendJoin<T>(ImmutableArray<T> items, Action<T, BuilderBase, StringifyContext> callback, Action? join = null)
        {
            join ??= () => { Append(','); Space(); };
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0)
                {
                    join.Invoke();
                }
                callback.Invoke(items[i], this, default);
            }
        }

        public void Indent()
        {
            if (!Minimize) Append(' ', Math.Max(0, IndentLevel * IndentSize));
        }
        public void Space()
        {
            if (!Minimize) Append(' ');
        }
        public void NewLine()
        {
            if (Minimize) return;
            Append(Environment.NewLine);
            Indent();
        }
    }

    public class BuilderStream : BuilderBase
    {
        readonly TextWriter _writer;

        public BuilderStream(TextWriter writer) => _writer = writer;

        public override void Append(char v) => _writer.Write(v);
        public override void Append(string v) => _writer.Write(v);
    }

    public class Builder : BuilderBase
    {
        readonly StringBuilder _builder = new();

        public override void Append(char v) => _builder.Append(v);
        public override void Append(string v) => _builder.Append(v);
        public override void Append(char v, int repeatCount) => _builder.Append(v, repeatCount);

        public override string ToString() => _builder.ToString();
    }

    public struct StringifyContext
    {
        public bool IsNaked;
    }
}
