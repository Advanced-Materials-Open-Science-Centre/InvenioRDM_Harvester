using System.Text;

namespace ConverterPoC;

public class Utf8StringWriter : StringWriter
{
    public Utf8StringWriter(StringBuilder sb) : base(sb) { }

    public override Encoding Encoding => Encoding.UTF8;
}