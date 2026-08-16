namespace XFEToolBox.Server.Core.Exceptions;

public sealed class ToolPackageValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
