"use strict";

const OUT_PREFIX = "CommunicationData_";


let user_guid = NewUUID();


var socket = undefined;
var input_loop = undefined;
var output_loop = undefined;
var incoming_queue = undefined;
var outgoing_queue = undefined;


// TODO


function decode_connection_string(conn_string)
{
    try
    {
        let parts = atob(conn_string).split('$');

        if (parts.length > 2)
            return parts[0] + ':' + parts[2];
    }
    catch (e)
    {
    }

    return undefined;
}

function socket_error()
{
    if (socket !== undefined && socket.readyState != WebSocket.OPEN)
    {
        $('#login-form').addClass('failed');

        socket_close();
    }
    else
    {
        alert("lol");
        // TODO : report error
    }
}

function socket_close()
{
    socket = undefined;
    incoming_queue = undefined;
    outgoing_queue = undefined;

    clearInterval(input_loop);
    clearInterval(output_loop);

    window.onbeforeunload = null;

    $('#login-form').removeClass('loading');
    $('#login-container').removeClass('hidden');
    $('html,body').removeClass('scrollable');
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

    window.onbeforeunload = () => 'Are you sure that you want to leave the game server?\n' +
                                'You will be logged out from the current game.';

    $('#login-container').addClass('hidden');
    $('html,body').addClass('scrollable');
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



$('#login-string').on('input change paste keyup', function()
{
    let input = $('#login-string').val().trim();
    let target = decode_connection_string(input);

    if (target !== undefined)
    {
        $('#login-start').removeClass('hidden');
        $('#login-hint').addClass('hidden');
    }
    else
    {
        $('#login-start').addClass('hidden');

        if (input.length > 0)
            $('#login-hint').removeClass('hidden');
    }
});

$('#login-string').keypress(function(e){
    if (e.keyCode == 13)
        $('#login-start').click();
});

$('#login-start').click(function()
{
    let target = decode_connection_string($('#login-string').val());

    if (socket === undefined && target !== undefined)
    {
        $('#login-form').addClass('loading');

        setTimeout(function()
        {
            incoming_queue = new Array();
            outgoing_queue = new Array();
            socket = new WebSocket(`ws://${target}`);
            socket.onclose = socket_close;
            socket.onopen = socket_open;
            socket.onmessage = e => incoming_queue.push(JSON.parse(e.data));
            socket.onerror = socket_error;
        }, 350);
    }
});

$('#login-failed-dismiss').click(() => $("#login-form").removeClass("failed"));

