namespace DataLinkNetwork2
{
    public enum Response
    {
        Undefined, // Неизвестен или не получен
        RR, // Receiver Ready
        RNR, // Receiver Not Ready
        REJ, // Reject
        SREJ // Selective Reject
    }
}