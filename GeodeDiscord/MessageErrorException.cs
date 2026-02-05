namespace GeodeDiscord;

public class MessageErrorException : Exception {
    public MessageErrorException() { }

    public MessageErrorException(string message) : base(message) { }

    // no inner exceptions allowed..
    // ReSharper disable UnusedParameter.Local
    // ReSharper disable once UnusedMember.Local
    private MessageErrorException(string message, Exception innerException) { }
    // ReSharper restore UnusedParameter.Local
}
