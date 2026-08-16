namespace XFEToolBox.Server.Core.Exceptions;

public sealed class ToolPackageConflictException(string message) : Exception(message);
