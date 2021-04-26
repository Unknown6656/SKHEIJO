var socket = undefined;
var server_name = undefined;
let user_guid = NewUUID();



let incoming_queue = new Array();
let outgoing_queue = new Array();


$("#conn-start").click(function()
{
    if (socket === undefined)
    {
        socket = new WebSocket(`ws://${$('#conn-target').val()}`);
        socket.onclose = socket_close;
        socket.onopen = socket_open;
    }
});

function socket_close()
{
    socket = undefined;
    server_name = undefined;
}

function socket_open()
{
    socket.send(new Blob([
        user_guid.bytes
    ]));
    socket.onmessage = (e) =>
    {
        socket.onmessage = socket_incoming;
        server_name = "" + e.data;

        on_server_connected();
    }
}

function on_server_connected()
{

}

function socket_incoming(event)
{
    incoming_queue.push(JSON.parse(event.data));
}

