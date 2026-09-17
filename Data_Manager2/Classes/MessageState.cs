namespace Data_Manager2.Classes
{
    public enum MessageState
    {
        SENDING,
        SEND,
        UNREAD,
        READ,
        DELIVERED,
        TO_ENCRYPT,
        // Sending an OMEMO message failed, because no session could get established:
        ENCRYPT_FAILED
    }
}
