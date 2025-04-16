using System;
using System.IO;
using System.Text;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    internal class MySqlScriptReader : IDisposable
    {
        private readonly TextReader _reader;
        public string Script { get; private set; }
        public MySqlScriptReader(TextReader reader)
        {
            _reader = reader;
        }

        public void Dispose()
        {
            Close();
        }

        private void Close()
        {
            _reader.Close();
            GC.SuppressFinalize(this);
        }

        public bool ReadScript()
        {
            var sb = new StringBuilder();

            while (true)
            {
                var line = _reader.ReadLine();

                if (line == null)
                    break;

                // MySQL uses semicolon (;) as the delimiter for separating statements.
                if (line.Trim().EndsWith(";"))
                {
                    sb.AppendLine(line);
                    Script = sb.ToString();
                    return true;
                }

                sb.AppendLine(line);
            }

            if (sb.Length <= 0)
                return false;

            Script = sb.ToString();
            return true;
        }
    }
}