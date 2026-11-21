namespace AppWrap.Build;
public sealed class BuildFailureException(string message,string details):Exception(message){public string Details{get;}=details;}
public sealed record BuildResult(string OutputPath,string Sha256,long Bytes,string? CleanupWarning=null);
