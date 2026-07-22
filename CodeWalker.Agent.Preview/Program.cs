using System;

namespace CodeWalker.Agent.Preview
{
    internal static class Program
    {
        // The .NET Framework host is intentionally separate so future off-screen rendering
        // can reuse CodeWalker.Peds/Renderer without coupling the MCP server to WinForms.
        [STAThread]
        private static int Main(string[] args)
        {
            Console.Error.WriteLine("CodeWalker.Agent.Preview requires a renderer job request.");
            return 2;
        }
    }
}
