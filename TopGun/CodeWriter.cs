using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace TopGun;

// all zero-based, end is exclusive
public readonly record struct TextPosition(int Line, int Column);

public class CodeWriter : IDisposable
{
    private readonly CodeWriter? parent;
    private readonly TextWriter writer;
    private readonly bool disposeWriter;
    private readonly int indent = 0;
    private readonly List<int> lineLengths = new();
    private bool needsIndent = true;
    private TextPosition position;

    public IReadOnlyList<int> LineLengths => parent?.lineLengths ?? lineLengths;
    public int TabSize { get; set; } = 4;
    public TextPosition Position
    {
        get => parent?.Position ?? position;
        private set
        {
            if (parent == null)
                position = value;
            else
                parent.Position = value;
        }
    }

    private bool NeedsIndent
    {
        get => parent?.needsIndent ?? needsIndent;
        set
        {
            if (parent == null)
                needsIndent = value;
            else
                parent.NeedsIndent = value;
        }
    }

    public CodeWriter(TextWriter writer, int indent = 0, bool disposeWriter = true)
    {
        this.writer = writer;
        this.indent = indent;
        this.disposeWriter = disposeWriter;
        NeedsIndent = true;
    }

    private CodeWriter(CodeWriter parent, TextWriter writer, int indent) : this(writer, indent, disposeWriter: false)
    {
        this.parent = parent;
    }

    public CodeWriter Indented => new(parent ?? this, writer, indent + 1);

    public void Dispose()
    {
        if (disposeWriter)
            writer.Dispose();
    }

    public void Write(char c)
    {
        WriteNecessaryIndent();
        writer.Write(c);
        AdvanceBy(c);
    }

    public void Write(string s)
    {
        var parts = s.Split('\n');
        foreach (var part in parts.SkipLast(1))
        {
            WriteNecessaryIndent();
            writer.Write(part);
            AdvanceBy(part);
            Write('\n');
        }
        WriteNecessaryIndent();
        writer.Write(parts.Last());
        AdvanceBy(parts.Last());
    }

    public void Write(object o) => Write(o.ToString() ?? "<null>");

    public void WriteLine()
    {
        Write('\n');
    }

    public void WriteLine(char c)
    {
        Write(c);
        Write('\n');
    }

    public void WriteLine(string s)
    {
        Write(s);
        Write('\n');
    }

    private void WriteNecessaryIndent()
    {
        if (NeedsIndent)
        {
            NeedsIndent = false;
            WriteIndent();
        }
    }

    private void WriteIndent()
    {
        var indent = this.indent;
        while (indent-- > 0)
            Write('\t');
    }

    private void AdvanceBy(char c)
    {
        if (c == '\n')
        {
            (parent?.lineLengths ?? lineLengths).Add(Position.Column);
            Position = new(Position.Line + 1, 0);
            NeedsIndent = true;
        }
        else if (c == '\t')
            Position = new(Position.Line, (Position.Column / TabSize + 1) * TabSize);
        else
            Position = new(Position.Line, Position.Column + 1);
    }

    private void AdvanceBy(string s)
    {
        foreach (var c in s)
        {
            if (c == '\n')
                throw new Exception("This should not have happened, new lines are to be processed only by AdvanceBy(char)");
            AdvanceBy(c);
        }
    }

    private void AdvanceToNextLine() => AdvanceBy('\n');
}
