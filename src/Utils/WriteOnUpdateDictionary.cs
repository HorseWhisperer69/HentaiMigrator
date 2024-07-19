using System.Text.Json;

namespace HentaiMigrator.Utils;

public class WriteOnUpdateDictionary<TKey,  TValue>: IEnumerable<KeyValuePair<TKey, TValue>>
{
    private readonly Dictionary<TKey, TValue> _data;
    public readonly string WritePath;
    public int Count { get; private set; }
    public IEnumerable<TKey> Keys { get { return _data.Keys; } }
    public IEnumerable<TValue> Values { get { return _data.Values; } }
    private static readonly JsonSerializerOptions SerializerOptions = new() {IncludeFields = true};

    public WriteOnUpdateDictionary(string writePath, bool initializeFromFile = true)
    {
        WritePath = writePath;

        if (File.Exists(writePath) && initializeFromFile)
        {
            _data = JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(File.ReadAllText(writePath), SerializerOptions);
            if (_data == null)
            {
                _data = new Dictionary<TKey, TValue>();
            }
            else
            {
                Count = _data.Count;
            }
        }
        else
        {
            _data = new Dictionary<TKey, TValue>();
            Count = 0;
        }
    }

    public void WriteToFile()
    {
        File.WriteAllText(WritePath + ".tmp", JsonSerializer.Serialize(_data, SerializerOptions));
            if (File.Exists(WritePath))
            {
                File.Delete(WritePath);
            }
        File.Move(WritePath + ".tmp", WritePath);
    }

    public TValue this[TKey key]
    {
        get
        {
            return _data[key];
        }
        set
        {
            if (!_data.ContainsKey(key)) Count++;
            _data[key] = value;

            WriteToFile();
        }
    }

    public void Add(TKey key, TValue value)
    {
        if (!_data.ContainsKey(key)) Count++;
        _data.Add(key, value);

        WriteToFile();
    }

    public bool Remove(TKey key)
    {
        if (_data.ContainsKey(key))
        {
            Count--;
            _data.Remove(key);
            WriteToFile();

            return true;
        } else
        {
            return false;
        }
    }

    public bool ContainsKey(TKey key)
    {
        return _data.ContainsKey(key);
    }

    public bool ContainsValue(TValue value)
    {
        return _data.ContainsValue(value);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        return _data.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return _data.GetEnumerator();
    }
}
