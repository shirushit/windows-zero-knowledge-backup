using BackupApp.Crypto;

namespace BackupApp.RestoreEngine;

public interface IKeyUnlockService
{
    MasterKey UnlockWithPassword(WrappedKeyEnvelope envelope, string password);
    MasterKey UnlockWithRecoveryKey(string formattedRecoveryKey, WrappedKeyEnvelope recoveryEnvelope);
}

public sealed class KeyUnlockService : IKeyUnlockService
{
    private readonly IRecoveryKeyService _recoveryKeyService;

    public KeyUnlockService(IRecoveryKeyService? recoveryKeyService = null)
    {
        _recoveryKeyService = recoveryKeyService ?? new RecoveryKeyService();
    }

    public MasterKey UnlockWithPassword(WrappedKeyEnvelope envelope, string password)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(password);

        return envelope.Unwrap(password);
    }

    public MasterKey UnlockWithRecoveryKey(string formattedRecoveryKey, WrappedKeyEnvelope recoveryEnvelope)
    {
        ArgumentNullException.ThrowIfNull(formattedRecoveryKey);
        ArgumentNullException.ThrowIfNull(recoveryEnvelope);

        return _recoveryKeyService.UnwrapMasterKeyWithRecoverySecret(formattedRecoveryKey, recoveryEnvelope);
    }
}
