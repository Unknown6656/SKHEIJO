"use strict";

const OUT_PREFIX = "CommunicationData_";


let user_guid = NewUUID();


var socket = undefined;
var input_loop = undefined;
var output_loop = undefined;
var incoming_queue = undefined;
var outgoing_queue = undefined;


// TODO



function socket_close()
{
    socket = undefined;
    incoming_queue = undefined;
    outgoing_queue = undefined;

    clearInterval(input_loop);
    clearInterval(output_loop);
}

function socket_open()
{
    socket.send(new Blob([
        user_guid.bytes
    ]));
    input_loop = setInterval(async () =>
    {
        if (incoming_queue != undefined && socket != undefined && socket.readyState == WebSocket.OPEN)
            while (incoming_queue.length > 0)
                await process_message(incoming_queue.shift());
        else
            socket_close();
    }, 5);
    output_loop = setInterval(async () =>
    {
        if (outgoing_queue != undefined && socket != undefined && socket.readyState == WebSocket.OPEN)
            while (outgoing_queue.length > 0)
                socket.send(outgoing_queue.shift());
        else
            socket_close();
    }, 5);
}

function send_message(message, type, conversation = undefined)
{
    if (outgoing_queue === undefined)
        return false;

    if (conversation === undefined)
        conversation = EmptyUUID();

    if (!("" + type).startsWith(OUT_PREFIX))
        type = OUT_PREFIX + type;

    let json = JSON.stringify({
        Type: type,
        FullType: type,
        Data: message,
        Guid: conversation.toString()
    });
    outgoing_queue.push(json);

    return true;
}

async function process_message(message)
{
    console.log(message);
}

$("#conn-start").click(function()
{
    if (socket === undefined)
    {
        incoming_queue = new Array();
        outgoing_queue = new Array();
        socket = new WebSocket(`ws://${$('#conn-target').val()}`);
        socket.onclose = socket_close;
        socket.onopen = socket_open;
        socket.onmessage = (e) => incoming_queue.push(JSON.parse(e.data));
    }
});
