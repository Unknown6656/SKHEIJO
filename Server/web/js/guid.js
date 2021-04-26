
function UUID(bytes)
{
    this.bytes = bytes === undefined ? new Uint8Array(16) : bytes;
}

UUID.prototype.toString = function()
{
    var str = '{';

    for (var i = 0; i < this.bytes.length; ++i)
    {
        str += ('0' + (this.bytes[i] & 0xFF).toString(16)).slice(-2);

        if (i == 3 || i == 5 || i == 7 || i == 9)
            str += '-';
    }

    return str + '}';
}

let EmptyUUID = () => new UUID();

let NewUUID = function()
{
    let uuid = new UUID();

    crypto.getRandomValues(uuid.bytes);
    uuid.bytes[6] = (uuid.bytes[6] & 0x0f) | 0x40;
    uuid.bytes[8] = (uuid.bytes[8] & 0x3f) | 0x80;

    return uuid;
}
