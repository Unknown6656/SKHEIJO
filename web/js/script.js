"use strict";

const TYPE_PREFIX = 'CommunicationData_';
const TYPE_SERVER_INFO = 'ServerInformation';
const TYPE_DISCONNECT = 'Disconnect';
const TYPE_NOTIFICATION = 'Notification';
const TYPE_PLAYER_JOINED_SERVER = 'PlayerJoined';
const TYPE_PLAYER_LEFT_SERVER = 'PlayerLeft';
const TYPE_PLAYER_JOINED_GAME = 'PlayerJoinedGame';
const TYPE_PLAYER_LEFT_GAME = 'PlayerLeftGame';
const TYPE_PLAYER_INFO_CHANGED = 'PlayerInfoChanged';
const TYPE_PLAYER_INFO_REQUEST = 'PlayerQueryInfo';
const TYPE_PLAYER_INFO = 'PlayerInfo';
const TYPE_JOIN_REQUEST = 'GameJoinRequest';
const TYPE_LEAVE_REQUEST = 'GameLeaveRequest';
const TYPE_NAME_REQUEST = 'PlayerNameChangeRequest';
const TYPE_GAME_UPDATE = 'GameUpdate';
const TYPE_KICK_PLAYER = 'AdminKickPlayer';
const TYPE_REMOVE_GAME_PLAYER = 'AdminRemovePlayerFromGame';
const TYPE_MAKE_ADMIN = 'AdminMakeAdmin';
const TYPE_MAKE_REGULAR = 'AdminMakeRegular';
const DISCONNECT_REASON_SHUTDOWN = 0;
const DISCONNECT_REASON_KICK = 1;
const GAMESTATE_STOPPED = 0;
const GAMESTATE_RUNNING = 1;
const GAMESTATE_FINAL_ROUND = 2;
const GAMESTATE_FINISHED = 3;
const STORAGE_CONN_STRING = 'conn-string';
const STORAGE_USER_NAME = 'user-name';
const STORAGE_USER_UUID = 'user-uuid';
const SERVER_TIMEOUT = 30_000;


let notifications = [ ];
let user_cache = { };
let server_name = undefined;

let socket = undefined;
let input_loop = undefined;
let output_loop = undefined;
let incoming_queue = undefined;
let outgoing_queue = undefined;
let server_conversations = { };
let notification_timeout = undefined;

let user_uuid = UUID.Parse(window.localStorage.getItem(STORAGE_USER_UUID));
let user_name = window.localStorage.getItem(STORAGE_USER_NAME);
let first_time = false;

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

let current_url = new URL(window.location.href);
let url_conn_string = current_url.searchParams.get("code");

if (url_conn_string == null)
    url_conn_string = window.localStorage.getItem(STORAGE_CONN_STRING);



const viewport = $("#viewport");

function on_page_resized()
{
    const attr = viewport.attr('content');
    const W800 = ',width=800';

    viewport.attr('content', screen.width < 800 ? attr + W800 : attr.replace(W800, ''));
}

function on_page_loaded()
{
    // TODO : check for outdated browser version !!!

    const min_screen_size = window.getComputedStyle(document.body).getPropertyValue('--min-width');

    on_page_resized();

    if (!window.matchMedia(`(max-device-width: ${min_screen_size})`).matches &&
        !window.matchMedia(`(max-device-height: ${min_screen_size})`).matches)
    {
        $('#min-width').html(min_screen_size);
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

        const min_height = parseInt(min_screen_size.replace('px', ''));

        if (screen.width < min_height)
            viewport.attr('content', `${viewport.attr('content')}, height=${min_height}`);
    }

    $('#login-string').val(url_conn_string);
    $('#login-string').focus();
    $('#login-string').select();
    on_login_input_changed();
}

function random(max)
{
    return Math.floor(Math.random() * max);
}

function generate_random_name()
{
    const prefix = ['red', 'green', 'blue', 'yellow', 'brown', 'white', 'pink', 'orange', 'turquoise', 'fast', 'large', 'slim',
        'indian', 'european', 'american', 'african', 'asian', 'australian', 'nordic', 'western', 'eastern', 'bright', 'happy',
        'dark', 'crimson', 'pale', 'medium', 'rare', 'angry', 'sliky', 'copper', 'iron', 'gold', 'steel', 'brass', 'wooden',
        'large', 'big', 'great', 'small', 'tall', 'tiny', 'petite', 'cold', 'chilly', 'bumpy', 'curly', 'dry', 'low', 'dusty',
        'flat', 'narrow', 'round', 'square', 'wide', 'straight', 'bendy', 'melodic', 'sweet', 'sour', 'salty', 'fuzzy', 'smooth',
        'young', 'old', 'wet', 'anxious', 'bored', 'annoyed', 'fierce', 'hungry', 'mysterious', 'calm', 'exited', 'kind', 'baby',
        'british', 'german', 'french', 'canadian', 'spanish', 'italian', 'chinese', 'japanese', 'swiss', 'polish', 'english',
        'korean', 'amber', 'beige', 'azure', 'bronze', 'teal', 'tanned',
    ];
    const suffix = ['fox', 'dog', 'cat', 'car', 'mouse', 'tiger', 'lion', 'eagle', 'pie', 'raptor', 'snake', 'turtle', 'salmon',
        'coral', 'smoke', 'tomato', 'pepper', 'salt', 'sugar', 'cake', 'tea', 'coffee', 'cucumber', 'apple', 'banana', 'peach',
        'orchid', 'tree', 'flower', 'stone', 'sea', 'forest', 'night', 'puma', 'falcon', 'horse', 'squirrel', 'yoda', 'olive',
        'rose', 'basilisc', 'dragon', 'bear', 'camel', 'bag', 'bee', 'tractor', 'coyote', 'crow', 'cricket', 'crocodile', 'gecko',
        'kiwi', 'leopard', 'lemur', 'pelican', 'penguin', 'swan'
    ];

    return `${prefix[random(prefix.length)]} ${suffix[random(suffix.length)]}`;
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
        $('#login-form').addClass('failed');
    else
        show_notification("A connection error occurred. You have been logged out.", false);

    socket_close();
}

function socket_close()
{
    on_server_leaving();

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
    $('body').removeClass('logged-in');
}

function socket_open()
{
    user_cache = { };
    server_name = undefined;

    update_game_field(null);

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

    show_username_select();
}

function server_send_command(type, data, conversation = undefined)
{
    if (outgoing_queue == undefined)
        return false;

    if (conversation == undefined)
        conversation = UUID.Empty;

    if (conversation instanceof UUID)
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
    if (type == TYPE_SERVER_INFO)
    {
        server_name = data.ServerName;

        data.Players.map(upate_user_info);

        update_server_and_player_info();
    }
    if (type == TYPE_PLAYER_JOINED_SERVER)
    {
        show_notification(data.UUID == user_uuid ? 'You have joined the server.' : 'A player has joined the server.');
        upate_user_info(data.UUID);
    }
    else if (type == TYPE_PLAYER_LEFT_SERVER)
    {
        show_notification(data.UUID == user_uuid ? 'You have left the server.' : 'A player has left the server.');
        upate_user_info(data.UUID);
    }
    else if (type == TYPE_PLAYER_JOINED_GAME)
    {
        if (data.UUID == user_uuid)
            show_notification('You have joined the game.');
        else
            show_notification(`${user_to_html(data.UUID)} has joined the game.`);

        upate_user_info(data.UUID);
    }
    else if (type == TYPE_PLAYER_LEFT_GAME)
    {
        if (data.UUID == user_uuid)
            show_notification('You have left the game.');
        else
            show_notification(`${user_to_html(data.UUID)} has left the game.`);

        upate_user_info(data.UUID);
    }
    else if (type == TYPE_PLAYER_INFO_CHANGED)
        upate_user_info(data.UUID);
    else if (type == TYPE_GAME_UPDATE)
        update_game_field(data);
    else if (type == TYPE_DISCONNECT)
        show_notification(
            data.Reason == DISCONNECT_REASON_SHUTDOWN ? 'You have been disconnected due to the server shutting down.'
            : data.Reason == DISCONNECT_REASON_KICK ? 'You have been kicked from the server by an administrator.'
            : 'You have been disconnected from the server. This might have occurred due to the server shutting down.'
            , false
        );
    else if (type == TYPE_NOTIFICATION)
        show_notification(data.Message, true);
    else
        console.log([type, data]);
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

    notification_timeout = setTimeout(hide_notification, 4000);

    update_notification_list();
}

function update_notification_list()
{
    let html = '<table>';

    for (const item of notifications)
        html += `
        <tr>
            <td class="status ${item.success ? '' : 'failure'}"></td>
            <td class="content">${item.content}</td>
            <td class="time">${new Date(item.time).toISOString().slice(0, 19).replace('T', ' ')}</td>
        </tr>`;

    html += '</table>';

    $('#notification-list').html(html);
}

function user_to_html(uuid)
{
    const user = user_cache[uuid];
    let name = '{Unknown Player}';
    let admin = false;

    if (user != undefined && user != null)
    {
        name = user.name;
        admin = user.admin;
    }

    return `
        <span class="player-name"
              data-uuid="${uuid}"
              ${admin ? 'data-admin' : ''}
              ${uuid == user_uuid ? 'data-is-me' : ''}>
            ${name}
        </span>
    `;
}

function card_to_html(card)
{
    if (card == undefined)
        card = null;

    if (card != null && card.hasOwnProperty('Value'))
        card = card.Value;

    return `<div class="card" data-value="${card}">${card == null ? '' : `<span>${card}</span>`}</div>`;
};

function update_game_field(data)
{
    $('#game-player-count, #draw-pile, #discard-pile, #game-state-text, #players, #ego-player').html('');
    $('#game-leave, #game-join, #admin-start-game, #admin-stop-game, #admin-reset-game').addClass('hidden');

    if (data == null || data == undefined)
        return;

    const divs = [];
    let index = 0;
    let joined = false;

    for (const player of data.Players)
    {
        const you = player.UUID == user_uuid;
        let card_index = 0;
        let points = 0;
        let row = '';

        for (const card of player.Cards)
        {
            if (card_index % player.Columns == 0)
                row += '<tr>';

            row += `<td class="pile" data-row="${Math.floor(card_index / player.Columns)}" data-col="${card_index % player.Columns}">${card_to_html(card)}</td>`;

            if ((card_index + 1) % player.Columns == 0)
                row += '</tr>';

            if (card != null)
                points += card;

            ++card_index;
        }

        const user_html = user_to_html(player.UUID);
        const user_name = $(user_html).text().trim();
        const html = `
            <div class="player${index == data.CurrentPlayer ? ' current' : ''}" data-uuid="${player.UUID}" data-name="${user_name}">
                <div class="player-cards">
                    <table class="player-grid">
                        ${row}
                    </table>
                    <div ${you ? 'id="currently-drawn"' : ''} class="pile" data-annotation="currently drawn">${player.HasDrawn ? card_to_html(you ? data.EgoDrawnCard : null) : ''}</div>
                </div>
                <div class="player-footer">
                    Player: &nbsp; ${user_html},
                    &nbsp;
                    Points: ${points}
                    <br/>
                    <span class="on-current">
                        It is currently ${you ? 'your' : user_name + "'s"} turn to play.
                    </span>
                </div>
            </div>
        `;

        if (you)
        {
            joined = true;

            $('#ego-player').html(html);
        }
        else
            divs.push(`<div class="other-player">${html}</div>`);

        ++index;
    }

    $('#players').html(divs.join('\n'));
    $('#game-player-count').html(data.Players.length);
    $('#draw-pile').html(data.DrawPileSize > 0 ? card_to_html(null) : '');
    $('#discard-pile').html(data.DiscardPileSize > 0 ? card_to_html(data.DiscardCard) : '');

    const state_txt = data.State == GAMESTATE_STOPPED ? 'Stopped'
                    : data.State == GAMESTATE_RUNNING ? 'Running'
                    : data.State == GAMESTATE_FINAL_ROUND ? 'Final Round'
                    : data.State == GAMESTATE_FINISHED ? 'Finished' : 'Unknown';

    $('#game-state-text').html(state_txt);

    if (joined)
        $('#game-leave').removeClass('hidden');
    else if (data.Players.length < data.MaxPlayers && data.State == GAMESTATE_STOPPED)
    {
        $('#game-join').removeClass('hidden');
        $('#ego-player').html(`
            <p>
                You are currently observing a (stopped) game.
                <br/>
                Use the [JOIN GAME]-button to join the game.
            </p>
        `);
    }

    $(data.State == GAMESTATE_FINISHED ? '#admin-reset-game' :
      data.State == GAMESTATE_STOPPED ? '#admin-start-game' : '#admin-stop-game').removeClass('hidden');
}

function hide_notification()
{
    if (notification_timeout !== undefined)
        clearTimeout(notification_timeout);

    notification_timeout = undefined;

    $('#notification-container').removeClass('visible');
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

function show_username_select()
{
    $('body').addClass('logged-in');
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

const preventDefault = e => e.preventDefault();

function on_server_joined()
{
    // document.body.addEventListener('touchmove', preventDefault, { passive: false });
}

function on_server_leaving()
{
    // document.body.removeEventListener('touchmove', preventDefault);

}

function update_server_and_player_info()
{
    let html = '<table>';
    let sorted = [];

    for (const uuid in user_cache)
        sorted.push({
            name: user_cache[uuid].name,
            admin: user_cache[uuid].admin,
            game: user_cache[uuid].game,
            uuid: uuid
        });

    sorted.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
    sorted.sort((a, b) => a.admin == b.admin ? 0 : a.admin ? -1 : 1);

    for (const user of sorted)
        html += `
        <tr>
            <td class="level ${user.admin ? 'admin' : ''}"></td>
            <td>
                ${user.name}
                ${user.uuid == user_uuid ? '<span>You</span>' : ''}
            </td>
            <td class="hidden">${user.game ? '<span>Joined Game</span>' : ''}</td>
            <td class="admin-only ${user.game ? '' : 'hidden'}">
                <button class="admin-kick-from-game" data-uuid="${user.uuid}">
                    Remove from game
                </button>
            </td>
            <td class="admin-only">
                <button class="admin-kick-from-server" data-uuid="${user.uuid}">
                    Kick from server
                </button>
            </td>
            <td class="admin-only">
                <button class="admin-make-${user.admin ? 'regular' : 'admin'}" data-uuid="${user.uuid}">
                    Make ${user.admin ? 'regular' : 'admin'} user
                </button>
            </td>
        </tr>`;

    html += '</html>';

    $('#player-count').html(sorted.length);
    $('#player-list').html(html);

    const reg_handler = (selector, type) => $(selector).click(e => server_send_command(type, { UUID: $(e.target).attr('data-uuid') }));

    reg_handler('.admin-kick-from-server[data-uuid]', TYPE_KICK_PLAYER);
    reg_handler('.admin-kick-from-game[data-uuid]', TYPE_REMOVE_GAME_PLAYER);
    reg_handler('.admin-make-regular[data-uuid]', TYPE_MAKE_REGULAR);
    reg_handler('.admin-make-admin[data-uuid]', TYPE_MAKE_ADMIN);
}

function upate_user_info(uuid)
{
    server_send_query(TYPE_PLAYER_INFO_REQUEST, {UUID: uuid}, (_, response) =>
    {
        if (!response.Exists)
            delete user_cache[uuid];
        else
        {
            if (user_cache[uuid] != undefined && uuid == user_uuid)
                if (!user_cache[uuid].admin && response.IsAdmin)
                    show_notification('You have been elevated to administrator.');
                else if (user_cache[uuid].admin && !response.IsAdmin)
                    show_notification('You have been removed as an administrator.', false);


            user_cache[uuid] = {
                name: response.Name,
                admin: response.IsAdmin,
                game: response.IsInGame
            };

            if (uuid == user_uuid)
                if (response.IsAdmin)
                    $('#main-container').addClass('admin');
                else
                    $('#main-container').removeClass('admin');
        }

        update_server_and_player_info();
    });
}

function change_username_req(name, callback)
{
    server_send_query(TYPE_NAME_REQUEST, {Name: name}, (_, d) => callback(d));
}



$('#login-string').on('input change paste keyup', on_login_input_changed);

$('#login-string').keypress(function(e) {
    if (e.keyCode == 13)
        $('#login-start').click();
});

$('#login-start').click(function()
{
    let string = $('#login-string').val();
    let target = decode_connection_string(string);
    let share_url = `${current_url.origin}${current_url.pathname}?code=${string}`;

    window.localStorage.setItem(STORAGE_CONN_STRING, string);

    $('#share-url').attr('href', share_url)
                   .html(share_url);

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

$('#username-input').keypress(function(e) {
    if (e.keyCode == 13)
        $('#username-apply').click();
});

$('#username-apply').click(() =>
{
    $('#username-error').html('');
    $('.menu-bar .menu-item[data-tab="game"]').click();

    change_username_req($('#username-input').val(), response =>
    {
        if (response.Success)
        {
            user_name = $('#username-input').val();
            window.localStorage.setItem(STORAGE_USER_NAME, user_name);

            $('#game-container').removeClass('hidden');
            $('#username-container').addClass('hidden');
            on_server_joined();
        }
        else
            $('#username-error').html(response.Message);
    });
});

$('.menu-bar .menu-item[data-tab]').click(function()
{
    const elem = $(this);
    const tab = elem.attr('data-tab');
    const content = $(`.menu-content-holder .menu-content[data-tab="${tab}"]`);

    elem.addClass('active');
    elem.siblings().removeClass('active');
    content.siblings().addClass('hidden');
    content.removeClass('hidden');
});

$('#logout-button').click(() => socket.close());

$('#reset-logout-button').click(function()
{
    window.localStorage.clear();
    socket.close();
    window.location.reload();
});

$('#game-join').click(function()
{
    $('#game-join').addClass('hidden');

    server_send_query(TYPE_JOIN_REQUEST, { }, (_, d) =>
    {
        if (!d.Success)
        {
            console.log(d);

            $('#game-join').removeClass('hidden');
        }
    });
});

$('#game-leave').click(function()
{
    $('#game-leave').addClass('hidden');

    server_send_command(TYPE_LEAVE_REQUEST, { });
});




const share_handler = (selector, callback) => $(`#share-container .share[data-service="${selector}"]`).click(() =>
    window.open(callback($('#share-url').text(), 'SKHEIJO game on ' + current_url.hostname), '_blank'));

// whatsapp://send/?text=
share_handler('whatsapp', (u, t) => `https://api.whatsapp.com/send?text=${encodeURI(`${t}\n${u}`)}`);
share_handler('twitter', (u, t) => `https://twitter.com/intent/tweet?url=${encodeURI(u)}&text=${encodeURI(t)}`);
share_handler('facebook', u => `https://www.facebook.com/sharer/sharer.php?u=${encodeURI(`${t}\n${u}`)}`);
share_handler('threema', u => `threema://compose?text=${encodeURI(`${t}\n${u}`)}`);
share_handler('reddit', (u, t) => `https://reddit.com/submit?url=${encodeURI(u)}&title=${encodeURI(t)}`);
share_handler('linkedin', u => `https://www.linkedin.com/sharing/share-offsite/?url=${encodeURI(u)}`);
share_handler('telegram', u => `https://telegram.me/share/url?url=${encodeURI(u)}&text=${encodeURI(t)}`);
share_handler('tumblr', (u, t) => `https://www.tumblr.com/widgets/share/tool?canonicalUrl=${encodeURI(u)}&title=${encodeURI(t)}&caption=${encodeURI(t)}`);
share_handler('pinterest', u => `http://pinterest.com/pin/create/link/?url=${encodeURI(u)}`);
share_handler('vk', u => `http://vk.com/share.php?url=${encodeURI(u)}&title=${encodeURI(t)}&comment=${encodeURI(t)}`);
share_handler('skype', (u, t) => `https://web.skype.com/share?url=${encodeURI(u)}&text=${encodeURI(t)}`);
share_handler('sms', (u, t) => `sms:+?body=${encodeURI(`${t}\n${u}`)}`);
share_handler('email', (u, t) => `mailto:?subject=${encodeURI(t)}&body=${encodeURI(u)}`);

if (navigator.share)
    $('#share-container .share[data-service="native"]').click(async () => await navigator.share({
        url: $('#share-url').text(),
        title: 'SKHEIJO game on ' + current_url.hostname
    }));


on_page_loaded();


