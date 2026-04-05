namespace LanguageCore;

public readonly struct Position :
    IEquatable<Position>
{
    public static Position UnknownPosition => new(new Range<SinglePosition>(SinglePosition.Undefined), new Range<int>(-1));
    public static Position Zero => new(new Range<SinglePosition>(SinglePosition.Zero), new Range<int>(0));

    public readonly Range<int> AbsoluteRange;
    public readonly Range<SinglePosition> Range;

    public Position this[Range range] => Slice(range);

    public bool IsValid => this != default && this != UnknownPosition;

    public Position(Range<SinglePosition> range, Range<int> absoluteRange)
    {
        Range = range;
        AbsoluteRange = absoluteRange;
    }

    public Position(ValueTuple<SinglePosition, SinglePosition> range, ValueTuple<int, int> absoluteRange)
    {
        Range = range;
        AbsoluteRange = absoluteRange;
    }

    public Position(params IPositioned?[] elements) : this(elements as IEnumerable<IPositioned?>) { }
    public Position(IPositioned item1)
    {
        Range = item1.Position.Range;
        AbsoluteRange = item1.Position.AbsoluteRange;
    }
    public Position(IEnumerable<IPositioned?> elements)
    {
        Range = UnknownPosition.Range;
        AbsoluteRange = UnknownPosition.AbsoluteRange;

        foreach (IPositioned? element in elements)
        {
            if (element is null) continue;
            Position position = element.Position;
            if (position == UnknownPosition) continue;
            Range = position.Range;
            AbsoluteRange = position.AbsoluteRange;
            break;
        }

        Position result = this;

        foreach (IPositioned? v in elements.Skip(1))
        {
            result = result.Union(v);
        }

        Range = result.Range;
        AbsoluteRange = result.AbsoluteRange;
    }

    public override string ToString()
    {
        if (Range.Start == Range.End) return Range.Start.ToStringMin();
        if (Range.Start.Line == Range.End.Line) return $"{Range.Start.Line + 1}:({Range.Start.Character}-{Range.End.Character})";
        return $"{Range.Start.ToStringMin()}-{Range.End.ToStringMin()}";
    }

    public Position Before() => new(new Range<SinglePosition>(new SinglePosition(Range.Start.Line, Range.Start.Character - 1), new SinglePosition(Range.Start.Line, Range.Start.Character)), new Range<int>(AbsoluteRange.Start - 1, AbsoluteRange.Start));

    public Position After() => new(new Range<SinglePosition>(new SinglePosition(Range.End.Line, Range.End.Character), new SinglePosition(Range.End.Line, Range.End.Character + 1)), new Range<int>(AbsoluteRange.End, AbsoluteRange.End + 1));

    public Position NextLine() => new(new Range<SinglePosition>(new SinglePosition(Range.End.Line + 1, 0), new SinglePosition(Range.End.Line + 1, 1)), new Range<int>(AbsoluteRange.End, AbsoluteRange.End + 1));

    public override bool Equals(object? obj) => obj is Position position && Equals(position);
    public bool Equals(Position other) => AbsoluteRange.Equals(other.AbsoluteRange) && Range.Equals(other.Range);

    public override int GetHashCode() => HashCode.Combine(AbsoluteRange, Range);

    public (Position Left, Position Right) Cut(int at)
    {
        if (Range.Start.Line != Range.End.Line)
        { throw new NotImplementedException($"Position slicing on different lines not implemented"); }

        if (at < 0) throw new ArgumentOutOfRangeException(nameof(at));
        int rangeSize = Range.End.Character - Range.Start.Character;

        if (rangeSize < 0)
        { throw new NotImplementedException($"Somehow end is larger than start"); }

        if (rangeSize < at)
        { throw new ArgumentOutOfRangeException(nameof(at), at, $"Slice location is larger than the range size ({rangeSize})"); }

        int rangeSizeAbs = AbsoluteRange.End - AbsoluteRange.Start;

        if (rangeSizeAbs < 0)
        { throw new NotImplementedException($"Somehow end is larger than start"); }

        if (rangeSizeAbs < at)
        { throw new ArgumentOutOfRangeException(nameof(at), at, $"Slice location is larger than the range size ({rangeSizeAbs})"); }

        Position left = new(
             new Range<SinglePosition>(
                Range.Start,
                new SinglePosition(Range.Start.Line, Range.Start.Character + at)
                ),
             new Range<int>(
                AbsoluteRange.Start,
                AbsoluteRange.Start + at
                )
            );

        Position right = new(
             new Range<SinglePosition>(
                left.Range.End,
                Range.End
                ),
             new Range<int>(
                left.AbsoluteRange.End,
                AbsoluteRange.End
                )
            );

        return (left, right);
    }

    public Position Slice(Range range)
    {
        if (Range.Start.Line != Range.End.Line)
        { throw new InvalidOperationException($"The position is on multiple lines"); }

        int absoluteLength = AbsoluteRange.End - AbsoluteRange.Start;

        (int start, int length) = range.GetOffsetAndLength(absoluteLength);

        return Slice(start, length);
    }

    public Position Slice(int start, int length)
    {
        if (Range.Start.Line != Range.End.Line)
        { throw new InvalidOperationException($"The position is on multiple lines"); }

        int absoluteStart = AbsoluteRange.Start + start;
        int columnStart = Range.Start.Character + start;
        int absoluteEnd = absoluteStart + length;
        int columnEnd = absoluteEnd + length;

        return new Position(
            new Range<SinglePosition>(
                new SinglePosition(Range.Start.Line, columnStart),
                new SinglePosition(Range.End.Line, columnEnd)
            ),
            new Range<int>(
                absoluteStart,
                absoluteEnd
            )
        );
    }

    public Position Union(Position other)
    {
        if (other == UnknownPosition) return this;
        if (this == UnknownPosition) return other;

        return new Position(
            RangeUtils.Union(Range, other.Range),
            RangeUtils.Union(AbsoluteRange, other.AbsoluteRange)
        );
    }

    public Position Union(params Position[] other)
    {
        if (other.Length == 0) return this;

        Position result = this;
        foreach (Position v in other) result = result.Union(v);
        return result;
    }

    public Position Union(IPositioned? other) => other is null ? this : Union(other.Position);

    public Position Union(params IPositioned?[] other)
    {
        if (other.Length == 0) return this;

        Position result = this;
        foreach (IPositioned? v in other) result = result.Union(v);
        return result;
    }

    public Position Union(IEnumerable<IPositioned?>? other)
    {
        if (other is null) return this;

        Position result = this;
        foreach (IPositioned? v in other) result = result.Union(v);
        return result;
    }

    public static bool operator ==(Position left, Position right) => left.Equals(right);
    public static bool operator !=(Position left, Position right) => !left.Equals(right);
}
