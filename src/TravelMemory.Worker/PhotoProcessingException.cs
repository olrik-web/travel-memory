namespace TravelMemory.Worker;

internal sealed class PhotoProcessingException(
    string code,
    string userMessage,
    bool isTransient,
    Exception? innerException = null)
    : Exception(userMessage, innerException)
{
    public string Code { get; } = code;

    public string UserMessage { get; } = userMessage;

    public bool IsTransient { get; } = isTransient;
}
