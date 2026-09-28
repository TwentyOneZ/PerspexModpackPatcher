using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PerspexCharacterAuthority;

public sealed class ExtensionPayload
{
    public int SchemaVersion;
    public byte[] Data;
}

public sealed class CharacterSnapshot
{
    public const int SchemaVersion = 1;
    public string AccountId;
    public long CharacterId;
    public string CharacterName;
    public long CreatedUtcTicks;
    public long UpdatedUtcTicks;
    public byte[] VanillaPlayerData;
    public Dictionary<string, ExtensionPayload> Extensions = new Dictionary<string, ExtensionPayload>(StringComparer.Ordinal);
}

public sealed class WorldProfileState
{
    public long WorldId;
    public bool HaveCustomSpawnPoint;
    public float SpawnX, SpawnY, SpawnZ;
    public bool HaveLogoutPoint;
    public float LogoutX, LogoutY, LogoutZ;
    public bool HaveDeathPoint;
    public float DeathX, DeathY, DeathZ;
    public float HomeX, HomeY, HomeZ;
    public byte[] MapData;
}

public static class PcaWorldProfileCodec
{
    public const int SchemaVersion = 1;

    public static byte[] Serialize(WorldProfileState state, int maxBytes)
    {
        if (state == null || state.WorldId == 0) throw new InvalidDataException("World profile identity is required.");
        if (state.MapData != null && state.MapData.Length > maxBytes) throw new InvalidDataException("World map data is too large.");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(SchemaVersion);
        writer.Write(state.WorldId);
        writer.Write(state.HaveCustomSpawnPoint);
        WriteVector(writer, state.SpawnX, state.SpawnY, state.SpawnZ);
        writer.Write(state.HaveLogoutPoint);
        WriteVector(writer, state.LogoutX, state.LogoutY, state.LogoutZ);
        writer.Write(state.HaveDeathPoint);
        WriteVector(writer, state.DeathX, state.DeathY, state.DeathZ);
        WriteVector(writer, state.HomeX, state.HomeY, state.HomeZ);
        writer.Write(state.MapData != null);
        if (state.MapData != null)
        {
            writer.Write(state.MapData.Length);
            writer.Write(state.MapData);
        }
        if (stream.Length > maxBytes) throw new InvalidDataException("World profile data is too large.");
        return stream.ToArray();
    }

    public static bool TryDeserialize(byte[] data, int maxBytes, out WorldProfileState state)
    {
        state = null;
        if (data == null || data.Length > maxBytes) return false;
        try
        {
            using var stream = new MemoryStream(data, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadInt32() != SchemaVersion) return false;
            var value = new WorldProfileState { WorldId = reader.ReadInt64() };
            value.HaveCustomSpawnPoint = reader.ReadBoolean();
            ReadVector(reader, out value.SpawnX, out value.SpawnY, out value.SpawnZ);
            value.HaveLogoutPoint = reader.ReadBoolean();
            ReadVector(reader, out value.LogoutX, out value.LogoutY, out value.LogoutZ);
            value.HaveDeathPoint = reader.ReadBoolean();
            ReadVector(reader, out value.DeathX, out value.DeathY, out value.DeathZ);
            ReadVector(reader, out value.HomeX, out value.HomeY, out value.HomeZ);
            if (reader.ReadBoolean())
            {
                var length = reader.ReadInt32();
                if (length < 0 || length > maxBytes) return false;
                value.MapData = reader.ReadBytes(length);
                if (value.MapData.Length != length) return false;
            }
            if (value.WorldId == 0 || stream.Position != stream.Length) return false;
            state = value;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException || ex is IOException || ex is ArgumentException)
        {
            return false;
        }
    }

    private static void WriteVector(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x); writer.Write(y); writer.Write(z);
    }

    private static void ReadVector(BinaryReader reader, out float x, out float y, out float z)
    {
        x = reader.ReadSingle(); y = reader.ReadSingle(); z = reader.ReadSingle();
    }
}

public sealed class AccountBinding
{
    public const int SchemaVersion = 1;
    public string AccountId;
    public long CharacterId;
    public string LastKnownCharacterName;
    public long BoundUtcTicks;
}

public static class PcaSnapshotCodec
{
    private const uint Magic = 0x31414350; // PCA1, little endian
    private const int HashLength = 32;
    private const int MaxStringBytes = 4096;
    private const int MaxExtensions = 32;

    public static byte[] Serialize(CharacterSnapshot snapshot, int maxBytes)
    {
        if (snapshot == null || snapshot.CharacterId == 0 || string.IsNullOrWhiteSpace(snapshot.AccountId))
            throw new InvalidDataException("Snapshot identity is required.");
        if (snapshot.VanillaPlayerData == null || snapshot.VanillaPlayerData.Length > maxBytes)
            throw new InvalidDataException("Snapshot player data is missing or too large.");

        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(CharacterSnapshot.SchemaVersion);
            WriteString(writer, snapshot.AccountId);
            writer.Write(snapshot.CharacterId);
            WriteString(writer, snapshot.CharacterName);
            writer.Write(snapshot.CreatedUtcTicks);
            writer.Write(snapshot.UpdatedUtcTicks);
            WriteBytes(writer, snapshot.VanillaPlayerData, maxBytes);
            if (snapshot.Extensions.Count > MaxExtensions) throw new InvalidDataException("Too many extensions.");
            writer.Write(snapshot.Extensions.Count);
            foreach (var pair in snapshot.Extensions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value == null) throw new InvalidDataException("Invalid extension.");
                WriteString(writer, pair.Key);
                writer.Write(pair.Value.SchemaVersion);
                WriteBytes(writer, pair.Value.Data ?? Array.Empty<byte>(), maxBytes);
            }
        }
        var raw = body.ToArray();
        if (raw.Length > maxBytes) throw new InvalidDataException("Snapshot envelope is too large.");
        var hash = Hash(raw);
        var output = new byte[raw.Length + hash.Length];
        Buffer.BlockCopy(raw, 0, output, 0, raw.Length);
        Buffer.BlockCopy(hash, 0, output, raw.Length, hash.Length);
        return output;
    }

    public static bool TryDeserialize(byte[] envelope, int maxBytes, out CharacterSnapshot snapshot, out string error)
    {
        snapshot = null;
        error = null;
        if (envelope == null || envelope.Length <= HashLength || envelope.Length > maxBytes + 65536)
        {
            error = "invalid envelope size";
            return false;
        }
        var bodyLength = envelope.Length - HashLength;
        var expected = Hash(envelope, 0, bodyLength);
        if (!FixedEquals(expected, envelope, bodyLength))
        {
            error = "checksum mismatch";
            return false;
        }
        try
        {
            using var body = new MemoryStream(envelope, 0, bodyLength, false);
            using var reader = new BinaryReader(body, Encoding.UTF8, true);
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("magic mismatch");
            if (reader.ReadInt32() != CharacterSnapshot.SchemaVersion) throw new InvalidDataException("unsupported schema");
            var value = new CharacterSnapshot
            {
                AccountId = ReadString(reader, MaxStringBytes),
                CharacterId = reader.ReadInt64(),
                CharacterName = ReadString(reader, MaxStringBytes),
                CreatedUtcTicks = reader.ReadInt64(),
                UpdatedUtcTicks = reader.ReadInt64(),
                VanillaPlayerData = ReadBytes(reader, maxBytes)
            };
            if (value.CharacterId == 0 || string.IsNullOrWhiteSpace(value.AccountId)) throw new InvalidDataException("invalid identity");
            var count = reader.ReadInt32();
            if (count < 0 || count > MaxExtensions) throw new InvalidDataException("invalid extension count");
            for (var i = 0; i < count; i++)
            {
                var key = ReadString(reader, MaxStringBytes);
                if (string.IsNullOrWhiteSpace(key) || value.Extensions.ContainsKey(key)) throw new InvalidDataException("invalid extension key");
                value.Extensions.Add(key, new ExtensionPayload { SchemaVersion = reader.ReadInt32(), Data = ReadBytes(reader, maxBytes) });
            }
            if (body.Position != body.Length) throw new InvalidDataException("trailing bytes");
            snapshot = value;
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException || ex is IOException || ex is InvalidDataException || ex is ArgumentException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var data = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (data.Length > MaxStringBytes) throw new InvalidDataException("String is too long.");
        writer.Write(data.Length);
        writer.Write(data);
    }

    private static string ReadString(BinaryReader reader, int maxLength)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > maxLength) throw new InvalidDataException("Invalid string length.");
        var data = reader.ReadBytes(length);
        if (data.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(data);
    }

    private static void WriteBytes(BinaryWriter writer, byte[] value, int maxLength)
    {
        if (value == null || value.Length > maxLength) throw new InvalidDataException("Invalid byte payload.");
        writer.Write(value.Length);
        writer.Write(value);
    }

    private static byte[] ReadBytes(BinaryReader reader, int maxLength)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > maxLength) throw new InvalidDataException("Invalid byte payload length.");
        var data = reader.ReadBytes(length);
        if (data.Length != length) throw new EndOfStreamException();
        return data;
    }

    private static byte[] Hash(byte[] data) => Hash(data, 0, data.Length);
    private static byte[] Hash(byte[] data, int offset, int count)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data, offset, count);
    }

    private static bool FixedEquals(byte[] expected, byte[] actual, int offset)
    {
        if (expected.Length != HashLength || actual.Length - offset != HashLength) return false;
        var different = 0;
        for (var i = 0; i < HashLength; i++) different |= expected[i] ^ actual[offset + i];
        return different == 0;
    }
}

public static class PcaBindingCodec
{
    private const uint Magic = 0x31424350; // PCB1

    public static byte[] Serialize(AccountBinding binding)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.AccountId) || binding.CharacterId == 0)
            throw new InvalidDataException("Binding identity is required.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Magic);
            writer.Write(AccountBinding.SchemaVersion);
            Write(writer, binding.AccountId);
            writer.Write(binding.CharacterId);
            Write(writer, binding.LastKnownCharacterName);
            writer.Write(binding.BoundUtcTicks);
        }
        var raw = stream.ToArray();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(raw);
        return raw.Concat(hash).ToArray();
    }

    public static bool TryDeserialize(byte[] data, out AccountBinding binding)
    {
        binding = null;
        if (data == null || data.Length < 48) return false;
        var length = data.Length - 32;
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(data, 0, length);
            if (!hash.SequenceEqual(data.Skip(length))) return false;
        }
        try
        {
            using var stream = new MemoryStream(data, 0, length, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != AccountBinding.SchemaVersion) return false;
            binding = new AccountBinding { AccountId = Read(reader), CharacterId = reader.ReadInt64(), LastKnownCharacterName = Read(reader), BoundUtcTicks = reader.ReadInt64() };
            return binding.CharacterId != 0 && !string.IsNullOrWhiteSpace(binding.AccountId) && stream.Position == stream.Length;
        }
        catch (Exception ex) when (ex is EndOfStreamException || ex is InvalidDataException || ex is ArgumentException) { return false; }
    }

    private static void Write(BinaryWriter writer, string value)
    {
        var data = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (data.Length > 4096) throw new InvalidDataException("Binding text is too long.");
        writer.Write(data.Length);
        writer.Write(data);
    }
    private static string Read(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > 4096) throw new InvalidDataException();
        var data = reader.ReadBytes(length);
        if (data.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(data);
    }
}

public sealed class PcaStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
    private readonly string root;
    private readonly int maxBytes;
    private readonly bool backupsEnabled;
    private readonly int maximumBackups;

    public PcaStore(string root, int maxBytes, bool backupsEnabled, int maximumBackups)
    {
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        this.maxBytes = maxBytes;
        this.backupsEnabled = backupsEnabled;
        this.maximumBackups = Math.Max(1, maximumBackups);
        Directory.CreateDirectory(root);
    }

    public bool TryAuthorize(string accountId, long characterId, string characterName, bool singleCharacter, out string denial)
    {
        denial = null;
        if (!ValidAccount(accountId) || characterId == 0) { denial = "Invalid character identity."; return false; }
        lock (GetLock("account:" + accountId))
        {
            if (!TryClaimCharacter(accountId, characterId, out denial)) return false;
            var bindingExists = File.Exists(BindingPath(accountId));
            if (!TryReadBinding(accountId, out var binding))
            {
                if (bindingExists) { denial = "Stored account binding is corrupt."; return false; }
                if (!singleCharacter) return true;
                binding = new AccountBinding { AccountId = accountId, CharacterId = characterId, LastKnownCharacterName = characterName ?? string.Empty, BoundUtcTicks = DateTime.UtcNow.Ticks };
                if (!WriteAtomic(BindingPath(accountId), PcaBindingCodec.Serialize(binding), false, 0)) { denial = "Could not persist account binding."; return false; }
                return true;
            }
            if (!string.Equals(binding.AccountId, accountId, StringComparison.Ordinal)) { denial = "Invalid stored account binding."; return false; }
            if (!singleCharacter) return true;
            if (binding.CharacterId == characterId)
            {
                if (!string.Equals(binding.LastKnownCharacterName, characterName ?? string.Empty, StringComparison.Ordinal))
                {
                    binding.LastKnownCharacterName = characterName ?? string.Empty;
                    if (!WriteAtomic(BindingPath(accountId), PcaBindingCodec.Serialize(binding), false, 0)) { denial = "Could not update account binding."; return false; }
                }
                return true;
            }
            denial = "This server allows only one character per account. Your account is already bound to: " + binding.LastKnownCharacterName;
            return false;
        }
    }

    public bool TryRead(string accountId, long characterId, out CharacterSnapshot snapshot, out bool recovered)
    {
        snapshot = null;
        recovered = false;
        lock (GetLock("character:" + accountId + ":" + characterId))
        {
            if (TryReadSnapshot(CurrentPath(accountId, characterId), out snapshot)) return true;
            var backups = BackupDirectory(accountId, characterId);
            if (!Directory.Exists(backups)) return false;
            foreach (var file in Directory.GetFiles(backups, "*.pca").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (!TryReadSnapshot(file, out snapshot)) continue;
                recovered = true;
                return true;
            }
            return false;
        }
    }

    public bool HasSnapshotData(string accountId, long characterId)
    {
        var current = CurrentPath(accountId, characterId);
        var backups = BackupDirectory(accountId, characterId);
        return File.Exists(current) || (Directory.Exists(backups) && Directory.GetFiles(backups, "*.pca").Length != 0);
    }

    public bool TrySave(CharacterSnapshot snapshot, out string error)
    {
        error = null;
        try
        {
            var bytes = PcaSnapshotCodec.Serialize(snapshot, maxBytes);
            lock (GetLock("character:" + snapshot.AccountId + ":" + snapshot.CharacterId))
            {
                if (!WriteAtomic(CurrentPath(snapshot.AccountId, snapshot.CharacterId), bytes, backupsEnabled, maximumBackups))
                {
                    error = "Atomic snapshot write failed.";
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryGetBinding(string accountId, out AccountBinding binding)
    {
        lock (GetLock("account:" + accountId)) return TryReadBinding(accountId, out binding);
    }

    public IReadOnlyList<AccountBinding> GetAccountBindings()
    {
        var directory = Path.Combine(root, "Accounts");
        if (!Directory.Exists(directory)) return Array.Empty<AccountBinding>();
        return Directory.GetFiles(directory, "*.pcb").Select(path =>
        {
            try { return PcaBindingCodec.TryDeserialize(File.ReadAllBytes(path), out var binding) ? binding : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }).Where(binding => binding != null).OrderBy(binding => binding.AccountId, StringComparer.Ordinal).ToList();
    }

    public bool TryUnbind(string accountId) => DeleteFile(BindingPath(accountId), "account:" + accountId);

    public bool TryBind(string accountId, long characterId, string characterName, out string error)
    {
        error = null;
        lock (GetLock("account:" + accountId))
        {
            if (!TryClaimCharacter(accountId, characterId, out error)) return false;
            return WriteAtomic(BindingPath(accountId), PcaBindingCodec.Serialize(new AccountBinding { AccountId = accountId, CharacterId = characterId, LastKnownCharacterName = characterName ?? string.Empty, BoundUtcTicks = DateTime.UtcNow.Ticks }), false, 0);
        }
    }

    public IReadOnlyList<CharacterSnapshot> GetCharacters(string accountId)
    {
        var directory = Path.Combine(root, "Characters", Token(accountId));
        if (!Directory.Exists(directory)) return Array.Empty<CharacterSnapshot>();
        return Directory.GetDirectories(directory).Select(d => CurrentPath(accountId, ParseCharacterFolder(d))).Where(File.Exists)
            .Select(p => TryReadSnapshot(p, out var value) ? value : null).Where(p => p != null).OrderBy(p => p.UpdatedUtcTicks).ToList();
    }

    public bool TryRestore(string accountId, long characterId, string backupFile, out string error)
    {
        error = null;
        var directory = BackupDirectory(accountId, characterId);
        var candidate = Path.Combine(directory, Path.GetFileName(backupFile ?? string.Empty));
        if (!candidate.EndsWith(".pca", StringComparison.OrdinalIgnoreCase) || !TryReadSnapshot(candidate, out var snapshot)) { error = "Valid backup not found."; return false; }
        if (!string.Equals(snapshot.AccountId, accountId, StringComparison.Ordinal) || snapshot.CharacterId != characterId) { error = "Backup identity does not match the target character."; return false; }
        if (!TryBackup(accountId, characterId, out error)) return false;
        return TrySave(snapshot, out error);
    }

    public bool TryBackup(string accountId, long characterId, out string error)
    {
        error = null;
        lock (GetLock("character:" + accountId + ":" + characterId))
        {
            var source = CurrentPath(accountId, characterId);
            if (!File.Exists(source)) { error = "Snapshot not found."; return false; }
            if (CopyBackup(source, BackupDirectory(accountId, characterId), Math.Max(2, maximumBackups))) return true;
            error = "Could not create backup.";
            return false;
        }
    }

    public bool TryDelete(string accountId, long characterId, out string error)
    {
        error = null;
        lock (GetLock("character:" + accountId + ":" + characterId))
        {
            var path = CurrentPath(accountId, characterId);
            if (!File.Exists(path)) { error = "Snapshot not found."; return false; }
            if (!CopyBackup(path, BackupDirectory(accountId, characterId), Math.Max(1, maximumBackups))) { error = "Could not create pre-delete backup."; return false; }
            File.Delete(path);
            return true;
        }
    }

    public string Export(string accountId, long characterId)
    {
        var source = CurrentPath(accountId, characterId);
        if (!TryReadSnapshot(source, out _)) return null;
        var directory = Path.Combine(root, "Exports");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, Token(accountId) + "_" + characterId + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".pca");
        File.Copy(source, output);
        return output;
    }

    public string[] GetBackups(string accountId, long characterId)
    {
        var directory = BackupDirectory(accountId, characterId);
        return !Directory.Exists(directory) ? Array.Empty<string>() : Directory.GetFiles(directory, "*.pca").OrderByDescending(File.GetLastWriteTimeUtc).Select(Path.GetFileName).ToArray();
    }

    private bool TryClaimCharacter(string accountId, long characterId, out string error)
    {
        error = null;
        lock (GetLock("owner:" + characterId))
        {
            var path = Path.Combine(root, "Owners", characterId + ".owner");
            if (File.Exists(path))
            {
                var owner = File.ReadAllText(path, Encoding.UTF8);
                if (!string.Equals(owner, accountId, StringComparison.Ordinal)) { error = "Character ID already belongs to another account."; return false; }
                return true;
            }
            return WriteAtomic(path, Encoding.UTF8.GetBytes(accountId), false, 0) || Fail(out error, "Could not persist character ownership.");
        }
    }

    private static bool Fail(out string error, string message) { error = message; return false; }
    private bool TryReadBinding(string accountId, out AccountBinding binding)
    {
        binding = null;
        var path = BindingPath(accountId);
        try { return File.Exists(path) && PcaBindingCodec.TryDeserialize(File.ReadAllBytes(path), out binding); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private bool TryReadSnapshot(string path, out CharacterSnapshot snapshot)
    {
        snapshot = null;
        try { return File.Exists(path) && PcaSnapshotCodec.TryDeserialize(File.ReadAllBytes(path), maxBytes, out snapshot, out _); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private bool WriteAtomic(string path, byte[] bytes, bool backup, int maxBackups)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (!File.Exists(path)) { File.Move(temporary, path); return true; }
            var backupDirectory = Path.Combine(Path.GetDirectoryName(path), "backups");
            if (backup && !CopyBackup(path, backupDirectory, int.MaxValue)) return false;
            File.Replace(temporary, path, null);
            if (backup) RotateBackups(backupDirectory, Math.Max(1, maxBackups));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool CopyBackup(string source, string directory, int maximum)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var name = DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + ".pca";
            File.Copy(source, Path.Combine(directory, name));
            foreach (var stale in Directory.GetFiles(directory, "*.pca").OrderByDescending(File.GetLastWriteTimeUtc).Skip(maximum)) File.Delete(stale);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private static void RotateBackups(string directory, int maximum)
    {
        try
        {
            foreach (var stale in Directory.GetFiles(directory, "*.pca").OrderByDescending(File.GetLastWriteTimeUtc).Skip(maximum)) File.Delete(stale);
        }
        catch (IOException ex) { System.Diagnostics.Trace.TraceWarning("PCA backup rotation failed: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.TraceWarning("PCA backup rotation failed: " + ex.Message); }
    }
    private bool DeleteFile(string path, string lockName)
    {
        lock (GetLock(lockName)) { if (!File.Exists(path)) return false; File.Delete(path); return true; }
    }
    private string CurrentPath(string accountId, long characterId) => Path.Combine(root, "Characters", Token(accountId), characterId.ToString(), "current.pca");
    private string BackupDirectory(string accountId, long characterId) => Path.Combine(root, "Characters", Token(accountId), characterId.ToString(), "backups");
    private string BindingPath(string accountId) => Path.Combine(root, "Accounts", Token(accountId) + ".pcb");
    private static object GetLock(string key) => Locks.GetOrAdd(key, _ => new object());
    private static bool ValidAccount(string accountId) => !string.IsNullOrWhiteSpace(accountId) && accountId.Length <= 512;
    private static string Token(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))).Replace("-", string.Empty);
    }
    private static long ParseCharacterFolder(string directory) => long.TryParse(Path.GetFileName(directory), out var value) ? value : 0;
}
