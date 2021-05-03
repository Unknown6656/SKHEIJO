"use strict";

const TYPE_PREFIX = 'CommunicationData_';
const STORAGE_CONN_STRING = 'conn-string';
const STORAGE_USER_NAME = 'user-name';
const STORAGE_USER_UUID = 'user-uuid';
const SERVER_TIMEOUT = 30_000;


var user_uuid = UUID.Parse(window.localStorage.getItem(STORAGE_USER_UUID));
var user_name = window.localStorage.getItem(STORAGE_USER_NAME);
var first_time = false;

if (user_name == null)
{
    first_time = true;
    user_name = generate_random_name();
    window.localStorage.setItem(STORAGE_USER_NAME, user_name);
}

if (user_uuid == null)
{
    user_uuid = UUID.New();
    window.localStorage.setItem(STORAGE_USER_UUID, user_uuid);
}

var notifications = [ ];


var socket = undefined;
var input_loop = undefined;
var output_loop = undefined;
var incoming_queue = undefined;
var outgoing_queue = undefined;
var server_conversations = { };
var notification_timeout = undefined;


if (!window.matchMedia('(max-device-width: 500px)').matches)
{
    $('#usage-container').remove();
    $('#login-container').removeClass('hidden');
}
else
{
    $('#usage-warning .hidden').removeClass('hidden');
    $('#usage-warning-dismiss').click(function()
    {
        $('#usage-container').remove();
        $('#login-container').removeClass('hidden');
    });
}

let current_url = new URL(window.location.href);
var url_conn_string = current_url.searchParams.get("code");

if (url_conn_string == null)
    url_conn_string = window.localStorage.getItem(STORAGE_CONN_STRING);

$('#login-string').val(url_conn_string);
$('#login-string').focus();
$('#login-string').select();
on_login_input_changed();



function random(max)
{
    return Math.floor(Math.random() * max);
}

function generate_random_name()
{
    const prefix = ['red', 'green', 'blue', 'yellow', 'brown', 'white', 'pink', 'orange', 'turqoise', 'fast', 'large', 'slim'];
    const suffix = ['fox', 'dog', 'cat', 'car', 'mouse', 'tiger', 'lion', 'eagle', 'pie', 'raptor', 'snake', 'turtle', 'salmon'];

    return `${prefix[random(prefix.length)]} ${suffix[random(suffix.length)]} ${1 + random(10)}`;
}

function decode_connection_string(conn_string)
{
    try
    {
        const parts = atob(conn_string).split('$');

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
        alert("error: socket closed and i dont know what to do");
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
    $('#username-container').addClass('hidden');
    $('#game-container').addClass('hidden');
}

function socket_open()
{
    socket.send(new Blob([
        user_uuid.bytes
    ]));
    input_loop = setInterval(() =>
    {
        if (incoming_queue != undefined && socket != undefined && socket.readyState == WebSocket.OPEN)
            while (incoming_queue.length > 0)
            {
                const message = incoming_queue.shift();
                const type = message.Type.startsWith(TYPE_PREFIX) ? message.Type.substring(TYPE_PREFIX.length) : message.Type;
                const data = message.Data;

                if (message.Conversation == UUID.Empty.toString())
                    process_server_message(type, data);
                else
                    server_conversations[message.Conversation] = { type: type, data: data };
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

    show_username_change();
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

    const json = JSON.stringify({
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
    const t_now = performance.now();
    const conversation = UUID.New().toString();
    const timeout = setInterval(function()
    {
        if (server_conversations[conversation] != undefined || performance.now() - t_now > SERVER_TIMEOUT)
        {
            clearInterval(timeout);

            const message = server_conversations[conversation];
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
            show_notification('A player has joined the server.');
            break;
        case 'PlayerLeft':
            show_notification('A player has left the server.');
            break;
        default:
            console.log(data);
    }

    // TODO

}

function show_notification(content, success = true)
{
    if (notification_timeout !== undefined)
        clearTimeout(notification_timeout);

    notification_timeout = undefined;
    notifications.push({ content: content, success: success, time: Date.now() });

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

function show_username_change()
{
    $('#login-container').addClass('hidden');
    $('#username-container').removeClass('hidden');
    $('#username-error').html('');
    $('#username-input').val(user_name);
    $('#username-input').focus();
    $('#username-input').select();

    if (first_time)
        $('#login-container').addClass('first-time');
    else
        $('#login-container').removeClass('first-time');
}


function comm_change_username(name, callback)
{
    server_send_query("PlayerNameChangeRequest", {Name: name}, (_, d) => callback(d));
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

    window.localStorage.setItem(STORAGE_CONN_STRING, string);

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

$('#username-apply').click(() =>
{
    $('#username-error').html('');
    comm_change_username($('#username-input').val(), response =>
    {
        if (response.Success)
        {
            user_name = $('#username-input').val();
            window.localStorage.setItem(STORAGE_USER_NAME, user_name);

            $('#game-container').removeClass('hidden');
            $('#username-container').addClass('hidden');
        }
        else
            $('#username-error').html(response.Message);
    });
});




