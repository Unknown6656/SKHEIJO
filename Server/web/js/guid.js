const __UUID_REGEX__ = /^\s*\{?\b[0-9a-f]{8}\b-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-\b[0-9a-f]{12}\b\}?\s*$/gi;

class UUID
{
    constructor(bytes)
    {
        this.bytes = bytes == undefined ? new Uint8Array(16) : bytes;
    }

    toString(include_curlies = false)
    {
        var str = include_curlies ? '{' : '';

        for (var i = 0; i < this.bytes.length; ++i)
        {
            str += ('0' + (this.bytes[i] & 0xFF).toString(16)).slice(-2);

            if (i == 3 || i == 5 || i == 7 || i == 9)
                str += '-';
        }

        if (include_curlies)
            str += '}';

        return str;
    }

    equals(obj)
    {
        return obj instanceof UUID && toString() == obj.toString();
    }
}

UUID.Empty = new UUID();

UUID.New = () =>
{
    let bytes = new Uint8Array(16);

    for (var i = 0; i < bytes.length; ++i)
    {
        bytes[i] = Math.floor(Math.random() * 256);

        if (i == 6)
            bytes[i] = (bytes[i] & 0x0f) | 0x40;
        else if (i == 8)
            bytes[i] = (bytes[i] & 0x3f) | 0x80;
    }

    return new UUID(bytes);
};

UUID.Parse = string =>
{
    let match = string.match(__UUID_REGEX__);

    if (match.length)
    {
        let bytes = new Uint8Array(match[0].match(/[0-9a-f]{2}/g).map(b => parseInt(b, 16)));

        return new UUID(bytes);
    }
    else
        return null;
};

