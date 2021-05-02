"use strict";

const TYPE_PREFIX = 'CommunicationData_';
const COOKIE_CONN_STRING = 'cookie-connection-string';
const SERVER_TIMEOUT = 30_000;


let user_guid = UUID.New();


var socket = undefined;
var input_loop = undefined;
var output_loop = undefined;
var incoming_queue = undefined;
var outgoing_queue = undefined;
var server_conversations = { };
var notification_timeout = undefined;


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
    input_loop = setInterval(() =>
    {
        if (incoming_queue != undefined && socket != undefined && socket.readyState == WebSocket.OPEN)
            while (incoming_queue.length > 0)
            {
                let message = incoming_queue.shift();
                let type = message.Type.trimStart(TYPE_PREFIX);
                let data = message.Data;

                if (!UUID.Empty.equals(UUID.Parse(message.Conversation)))
                    server_conversations[message.Conversation] = { type: type, data: data };
                else
                    process_server_message(type, data);
            }
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

function server_send_command(type, data, conversation = undefined)
{
    if (outgoing_queue == undefined)
        return false;

    if (conversation == undefined)
        conversation = UUID.Empty;
    else if (conversation instanceof UUID)
        conversation = conversation.toString();

    server_conversations[conversation] = undefined;

    if (!("" + type).startsWith(TYPE_PREFIX))
        type = TYPE_PREFIX + type;

    let json = JSON.stringify({
        Data: data,
        Type: type,
        FullType: type,
        Conversation: conversation
    });
    outgoing_queue.push(json);

    return true;
}

// callback accepts two params: type and data
function server_send_query(type, data, _callback)
{
    let t_now = performance.now();
    let conversation = UUID.New().toString();
    let timeout = setInterval(function()
    {
        if (server_conversations[conversation] != undefined || performance.now() - t_now > SERVER_TIMEOUT)
        {
            clearInterval(timeout);

            let message = server_conversations[conversation];
            delete server_conversations[conversation];

            if (message != undefined)
                _callback(message.type, message.data);
        }
    }, 5);

    server_send_command(type, data, conversation);
}

function process_server_message(type, data)
{
    switch (type)
    {
        case 'PlayerJoined':
            show_notification(`Player ${data.UUID} joined!`);
            break;
        case 'PlayerLeft':
            show_notification(`Player ${data.UUID} left!`);
            break;
        default:
            console.log(message);
    }

    // TODO

}

function show_notification(content, success = true)
{
    if (notification_timeout !== undefined)
        clearTimeout(notification_timeout);

    notification_timeout = undefined;

    if (success)
        $('#notification-container').removeClass('error');
    else
        $('#notification-container').addClass('error');

    $('#notification-content').html(content);
    $('#notification-container').addClass('visible');

    notification_timeout = setTimeout(hide_notification, 6000);
}

function hide_notification()
{
    if (notification_timeout !== undefined)
        clearTimeout(notification_timeout);

    notification_timeout = undefined;

    $('#notification-content').html('');
    $('#notification-container').removeClass('visible');
    $('#notification-container').removeClass('error');
}

function on_login_input_changed()
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
}


$('#login-string').on('input change paste keyup', on_login_input_changed);

$('#login-string').keypress(function(e){
    if (e.keyCode == 13)
        $('#login-start').click();
});

$('#login-start').click(function()
{
    let string = $('#login-string').val();
    let target = decode_connection_string(string);

    Cookies.set(COOKIE_CONN_STRING, string);

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

$('#login-failed-dismiss').click(() => $('#login-form').removeClass('failed'));

$('#notification-close').click(hide_notification);




$('#login-string').val(Cookies.get(COOKIE_CONN_STRING));
$('#login-string').focus();
$('#login-string').select();
on_login_input_changed();
