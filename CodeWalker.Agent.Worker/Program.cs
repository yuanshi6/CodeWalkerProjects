// Background worker entry point. Expensive indexing and preview jobs are deliberately
// isolated from the stdio MCP process so a failed render cannot corrupt its protocol stream.
var request = Console.In.ReadToEnd();
if (string.IsNullOrWhiteSpace(request)) { Console.Error.WriteLine("Worker expects a JSON job on stdin."); return 2; }
Console.Out.WriteLine("{\"success\":false,\"code\":\"JOB_NOT_IMPLEMENTED\",\"message\":\"No background job handler was selected.\"}");
return 1;
