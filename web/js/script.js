"use strict";

const UUID_REGEX = /\{\{[0-9A-F]{8}[-]?(?:[0-9A-F]{4}[-]?){3}[0-9A-F]{12}\}\}/gi;
const B64_ROTATE = 'E=k1bq7Rgc+jXypOKsft2iQwHv0dSxWTJLDGa56uAIB/zYNrZ3oPm9n4FVMCehlU8';
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
const TYPE_GAME_START = 'AdminGameStart';
const TYPE_GAME_STOP = 'AdminGameStop';
const TYPE_GAME_RESET = 'AdminGameReset';
const TYPE_KICK_PLAYER = 'AdminKickPlayer';
const TYPE_REMOVE_GAME_PLAYER = 'AdminRemovePlayerFromGame';
const TYPE_MAKE_ADMIN = 'AdminMakeAdmin';
const TYPE_MAKE_REGULAR = 'AdminMakeRegular';
const TYPE_SERVER_STOP = 'AdminServerStop';
const TYPE_GAME_DRAW = 'GameDraw';
const TYPE_GAME_SWAP = 'GameSwap';
const TYPE_GAME_DISCARD = 'GameDiscard';
const TYPE_GAME_UNCOVER = 'GameUncover';
const TYPE_PLAYER_WIN = 'PlayerWin';
const TYPE_LEADERBOARD = 'LeaderBoard';
const TYPE_BOARD_SIZE = 'AdminInitialBoardSize';
const TYPE_REQ_WIN_ANIM = 'AdminRequestWinAnimation';
const TYPE_ANIM_CARD_MOVE = 'AnimateMoveCard';
const TYPE_ANIM_CARD_FLIP = 'AnimateFlipCard';
const TYPE_ANIM_COLUMN = 'AnimateColumnDeletion';
const TYPE_HIGH_SCORES = 'ServerHighScores';
const TYPE_FINAL_ROUND = 'FinalRound';
const TYPE_SEND_CHAT = 'SendChatMessage';
const TYPE_CHAT_MENTION = 'ChatMessageMention';
const TYPE_CHAT_UPDATE = 'ChatMessages';
const WAITINGFOR_DRAW = 0;
const WAITINGFOR_PLAY = 1;
const WAITINGFOR_DISCARD = 2;
const WAITINGFOR_UNCOVER = 3;
const WAITINGFOR_NEXT = 4;
const PILE_DRAW = 0;
const PILE_DISCARD = 1;
const PILE_CURRENT = 2;
const PILE_USER = 3;
const DISCONNECT_REASON_SHUTDOWN = 0;
const DISCONNECT_REASON_KICK = 1;
const GAMESTATE_STOPPED = 0;
const GAMESTATE_RUNNING = 1;
const GAMESTATE_FINAL_ROUND = 2;
const GAMESTATE_FINISHED = 3;
const STORAGE_CONN_STRING = 'conn-string';
const STORAGE_USER_NAME = 'user-name';
const STORAGE_USER_UUID = 'user-uuid';
const SERVER_TIMEOUT = 20_000; // 20sec timeout


let confetti_ego = confetti.create($('#confetti-ego')[0], { resize: true });
let confetti_other = undefined;

let notifications = [ ];
let user_cache = { };
let server_name = undefined;

let socket = undefined;
let input_loop = undefined;
let output_loop = undefined;
let incoming_queue = undefined;
let outgoing_queue = undefined;
let server_conversations = { };
let notification_backlog = [ ];
let chat_messages_backlog = [ ];
let notification_timeout = undefined;
let initial_board_size = undefined;

let user_uuid_obj = UUID.Parse(window.localStorage.getItem(STORAGE_USER_UUID));
let user_name = window.localStorage.getItem(STORAGE_USER_NAME);
let first_time = false;


if (user_name == null)
{
    first_time = true;
    user_name = generate_random_name();
    window.localStorage.setItem(STORAGE_USER_NAME, user_name);
}

if (user_uuid_obj == null)
{
    user_uuid_obj = UUID.New();
    window.localStorage.setItem(STORAGE_USER_UUID, user_uuid_obj);
}

let user_uuid = user_uuid_obj.toString();

$('#user-uuid').html(`{${user_uuid}}`);


let current_url = new URL(window.location.href);
let url_conn_string = current_url.searchParams.get("code");

if (url_conn_string == null)
    url_conn_string = window.localStorage.getItem(STORAGE_CONN_STRING);


on_page_loaded = () =>
{
    $('#login-string').val(url_conn_string);
    $('#login-string').focus();
    $('#login-string').select();
    on_login_input_changed();
    start_notification_loop();
    on_page_resized();

    window.addEventListener('resize', on_page_resized);
}

function on_page_resized()
{
    const W800 = ',width=800';
    var attr = $('#viewport').attr('content').replace(W800, '');

    if (window.innerWidth < 800)
        attr += W800;

    $('#viewport').attr('content', attr);
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function random(max)
{
    return Math.floor(Math.random() * max);
}

function generate_random_name()
{
    const adjective = ['ultra-', 'really', 'extremely', 'mostly', 'semi-', 'sometimes', 'sporadically', 'rarely', 'extra-',
        'definitely', 'pseudo-', 'predominantly', 'usually', 'essentially', 'largely', 'unsatisfyingly', 'satisfyingly',
        'peculiarly', 'especially', 'notably', 'principally', 'explicitly', 'implicitly', 'laughably', 'generally', '', '',
        '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', '', ''
    ];
    const prefix = ['red', 'green', 'blue', 'yellow', 'brown', 'white', 'pink', 'orange', 'turquoise', 'fast', 'large', 'slim',
        'indian', 'european', 'american', 'african', 'asian', 'australian', 'nordic', 'western', 'eastern', 'bright', 'happy',
        'dark', 'crimson', 'pale', 'medium', 'rare', 'angry', 'sliky', 'copper', 'iron', 'gold', 'steel', 'brass', 'wooden',
        'large', 'big', 'great', 'small', 'tall', 'tiny', 'cold', 'chilly', 'bumpy', 'curly', 'dry', 'low', 'dusty', 'unsafe',
        'flat', 'narrow', 'round', 'square', 'wide', 'straight', 'bendy', 'melodic', 'sweet', 'sour', 'salty', 'fuzzy', 'smooth',
        'young', 'old', 'wet', 'anxious', 'bored', 'annoyed', 'fierce', 'hungry', 'mysterious', 'calm', 'exited', 'kind', 'baby',
        'british', 'german', 'french', 'canadian', 'spanish', 'italian', 'chinese', 'japanese', 'swiss', 'hungarian', 'english',
        'korean', 'amber', 'beige', 'azure', 'bronze', 'teal', 'tanned', 'default', 'standard', 'somber', 'ballistic', 'crawling',
        'flying', 'crouching', 'grumpy', 'optimistic', 'pessimistic', 'monochrome', 'noble', 'polished', 'sneaking', 'evil', 'funny',
        'exotic', 'sad', 'confused', 'sleepy', 'running', 'walking'
    ];
    const suffix = ['fox', 'dog', 'cat', 'car', 'mouse', 'tiger', 'lion', 'eagle', 'pie', 'raptor', 'snake', 'turtle', 'salmon',
        'coral', 'smoke', 'tomato', 'pepper', 'salt', 'sugar', 'cake', 'tea', 'coffee', 'cucumber', 'apple', 'banana', 'peach',
        'orchid', 'tree', 'flower', 'stone', 'sea', 'forest', 'night', 'puma', 'falcon', 'horse', 'squirrel', 'yoda', 'olive',
        'rose', 'basilisc', 'dragon', 'bear', 'camel', 'bag', 'bee', 'tractor', 'coyote', 'crow', 'cricket', 'crocodile', 'gecko',
        'kiwi', 'leopard', 'lemur', 'pelican', 'penguin', 'swan', 'cube', 'matter', 'square', 'magpie', 'eggplant', 'coconut',
        'avocado', 'cranberry', 'pine', 'oak', 'gandalf', 'gollum', 'voldemort', 'lamp', 'light', 'roof', 'mushroom', 'emperor',
        'king', 'queen', 'president', 'joker', 'teacher', 'coach', 'driver', 'pilot'
    ];
    let p = '', s = '', a = adjective[random(adjective.length)];

    while (p == s)
    {
        p = prefix[random(prefix.length)];
        s = suffix[random(suffix.length)];
    }

    if (a.length > 0 && !a.endsWith('-'))
        a += ' ';

    return `${a}${p} ${s}`;
}

function decode_connection_string(conn_string)
{
    try
    {
        let rotated = [];

        conn_string = conn_string.replace(' ', '+').replace('-', '/').replace('_', '=');

        for (var i = 0; i < conn_string.length; ++i)
            rotated.push(B64_ROTATE[(B64_ROTATE.length * 20 + conn_string.length + B64_ROTATE.indexOf(conn_string[i]) - 1 - i) % B64_ROTATE.length]);

        rotated = rotated.join('');

        const parts = atob(rotated).split('$');

        if (parts.length > 2)
            return {
                address: parts[0],
                ws: parts[1],
                wss: parts[2],
            };
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
    socket = undefined;
    incoming_queue = undefined;
    outgoing_queue = undefined;
    initial_board_size = undefined;
    chat_messages_backlog = [ ];

    clearInterval(input_loop);
    clearInterval(output_loop);

    window.location.hash = '';
    window.onbeforeunload = null;

    $('#login-form').removeClass('loading');
    $('#login-container').removeClass('hidden');
    $('#username-container').addClass('hidden');
    $('#game-container').addClass('hidden');
    $('body').removeClass('ready');
}

function socket_open()
{
    user_cache = { };
    server_name = undefined;

    update_game_field(null);

    socket.send(new Blob([
        user_uuid_obj.bytes
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
        if (data.UUID != user_uuid)
            show_notification('A player has joined the server.');

        upate_user_info(data.UUID);
    }
    else if (type == TYPE_PLAYER_LEFT_SERVER)
    {
        if (data.UUID != user_uuid)
            show_notification('A player has left the server.');

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
        show_notification(data.Message, data.Success);
    else if (type == TYPE_LEADERBOARD)
        update_leaderboard(data.LeaderBoard);
    else if (type == TYPE_HIGH_SCORES)
        update_server_highscores(data.HighScores);
    else if (type == TYPE_PLAYER_WIN)
        show_celebration(data.UUID);
    else if (type == TYPE_ANIM_CARD_MOVE)
        animate_card_move(data);
    else if (type == TYPE_ANIM_CARD_FLIP)
        animate_card_flip(data);
    else if (type == TYPE_ANIM_COLUMN)
        animate_column_deletion(data);
    else if (type == TYPE_BOARD_SIZE)
    {
        initial_board_size = { columns: data.Columns, rows: data.Rows };

        if ($('.menu-bar .menu-item[data-tab].active').attr('data-tab') != 'settings')
        {
            $('#init-board-cols').val(initial_board_size.columns);
            $('#init-board-rows').val(initial_board_size.rows);
        }
    }
    else if (type == TYPE_FINAL_ROUND)
        show_notification(`Final game round!<br/>${user_to_html(data.UUID)}'s score will be doubled if they loose.`);
    else if (type == TYPE_CHAT_MENTION)
        show_notification(`${user_to_html(data.UUID)} has mentioned you in a chat message.`);
    else if (type == TYPE_CHAT_UPDATE)
    {
        for (const message of data.Messages)
            chat_messages_backlog.push(message);

        update_chat_messages();
    }
    else
        console.log('unprocessed:', type, data);
}

function show_notification(content, success = true)
{
    const notf = { content: content, success: success, time: Date.now() };

    notification_backlog.push(notf);
    notifications.push(notf);

    update_notification_list();
}

function start_notification_loop()
{
    update_notification_list();
    setTimeout(async () =>
    {
        while (true)
        {
            if (notification_backlog != undefined)
                while (notification_backlog.length > 0)
                {
                    while (notification_timeout != undefined)
                        await sleep(250);

                    const notf = notification_backlog.shift();

                    if (notf.success)
                        $('#notification-container').removeClass('error');
                    else
                        $('#notification-container').addClass('error');

                    $('#notification-content').html(notf.content);
                    $('#notification-container').addClass('visible');

                    notification_timeout = setTimeout(hide_notification, 4000);
                }

            await sleep(150);
        }
    }, 0);
}

function hide_notification()
{
    if (notification_timeout !== undefined)
        clearTimeout(notification_timeout);

    notification_timeout = undefined;

    $('#notification-container').removeClass('visible');
}

function update_notification_list()
{
    let html = '';

    if (notifications.length == 0)
        html = '<i>You currently do not seem to have any notifications.</i>';
    else
    {
        for (const item of notifications)
            html = `
                <tr>
                    <td class="status ${item.success ? '' : 'failure'}"></td>
                    <td class="content">${item.content}</td>
                    <td class="time">${new Date(item.time).toISOString().slice(0, 19).replace('T', ' ')}</td>
                </tr>
            ${html}`;

        html = `
            <table>
                <tr id="last-notification"></tr>
                ${html}
            </table>
        `;
    }

    $('#notification-list').html(html);

    if (notifications.length > 0)
    {
        $('#last-notification')[0].scrollIntoView();
        $('.menu-item[data-tab="notifications"]').attr('data-count', notifications.length);
    }
    else
        $('.menu-item[data-tab="notifications"]').removeAttr('data-count');
}

function update_leaderboard(leaderboard)
{
    let html = '';

    for (var i = 0; i < leaderboard.length; ++i)
        html += `
            <tr>
                <td ${i < 3 ? `class="rank" data-rank="${i + 1}"` : ''}></td>
                <td>${i + 1}</td>
                <td>${leaderboard[i].Points}</td>
                <td>${user_to_html(leaderboard[i].UUID)}</td>
            </tr>
        `;

    $('#leaderboard-list').html(`
        <table>
            <tr>
                <th></th>
                <th>Rank</th>
                <th>Points</th>
                <th>Player</th>
            </tr>
            ${html}
        </table>
    `);
}

function update_server_highscores(highscores)
{
    let html = '';

    for (var i = 0; i < highscores.length; ++i)
        html += `
            <tr>
                <td ${i < 3 ? `class="rank" data-rank="${i + 1}"` : ''}></td>
                <td>${i + 1}</td>
                <td>${highscores[i].Points}</td>
                <td>${user_cache[highscores[i].UUID] == undefined ? highscores[i].LastName : user_to_html(highscores[i].UUID)}</td>
                <td>${highscores[i].Date.slice(0,19).replace('T', ', ')}</td>
                <td>${highscores[i].Players}</td>
                <td>${highscores[i].Rows} x ${highscores[i].Columns}</td>
            </tr>
        `;

    $('#highscore-list').html(`
        <table>
            <tr>
                <th></th>
                <th>Rank</th>
                <th>Highscore</th>
                <th>Player Name</th>
                <th>Date</th>
                <th>Player Count</th>
                <th>Game Size</th>
            </tr>
            ${html}
        </table>
    `);
}

function get_pile_selector(pile, uuid)
{
    if (pile.Pile == PILE_DRAW)
        return '#draw-pile';
    else if (pile.Pile == PILE_DISCARD)
        return '#discard-pile';
    else
    {
        const selector = `.player[data-uuid="${uuid}"] .pile`;

        if (pile.Pile == PILE_CURRENT)
            return `${selector}[data-pile="current"]`;
        else if (pile.Pile == PILE_USER)
            return `${selector}[data-pile="user-${pile.OptionalRow}-${pile.OptionalColumn}"]`;
    }
}

function get_card_position(pile, scale)
{
    let card = undefined;

    if (pile.hasClass("pile"))
        card = pile.find(".card")[0];

    const bounds = (card || pile[0]).getBoundingClientRect();
    const position = {
        top: bounds.top,
        left: bounds.left
    };

    if (pile.hasClass("pile") && card == undefined)
    {
        const border = 1 + (+window.getComputedStyle($('.pile')[0]).getPropertyValue('padding').replace('px', ''));

        position.top += border * scale;
        position.left += border * scale;
    }

    return position;
}

function animate_card_move(data)
{
    const from = get_pile_selector(data.From, data.UUID);
    const to = get_pile_selector(data.To, data.UUID);
    let from_scale = 1.0;
    let to_scale = 1.0;

    if (data.UUID != user_uuid)
    {
        const small = window.getComputedStyle(document.body).getPropertyValue('--player-scale');

        from_scale = from.indexOf('.player') == -1 ? 1.0 : +small;
        to_scale = to.indexOf('.player') == -1 ? 1.0 : +small;
    }

    const card_a = $(card_to_html(data.Card, 'animated'));
    const card_b = $(card_to_html(data.Behind, 'animated'));
    const anim_length = 750.0; // ms
    const from_pile = $(from);
    const to_pile = $(to);
    const from_pos = get_card_position(from_pile, from_scale);
    const to_pos = get_card_position(to_pile, to_scale);

    if (data.From.Pile == PILE_DRAW || data.From.Pile == PILE_DISCARD ||
        data.To.Pile == PILE_DRAW || data.To.Pile == PILE_DISCARD)
        $('#animation-pile').append(card_b);

    $('#animation-pile').append(card_a);

    from_pile.find('.card').hide();
    to_pile.find('.card').hide();

    card_a.css('left', from_pos.left + 'px');
    card_a.css('top', from_pos.top + 'px');

    if (data.From.Pile == PILE_DRAW || data.From.Pile == PILE_DISCARD)
    {
        card_b.css('left', from_pos.left + 'px');
        card_b.css('top', from_pos.top + 'px');
    }
    else
    {
        card_b.css('left', to_pos.left + 'px');
        card_b.css('top', to_pos.top + 'px');
    }

    card_a.css('transform', `scale(${from_scale})`);
    card_a.animate({
        left: to_pos.left + 'px',
        top: to_pos.top + 'px'
    },
    {
        duration: anim_length,
        step: function(now, fx) {
            now /= anim_length;

            $(this).css('transform', `scale(${now * to_scale + (1 - now) * from_scale})`);
            //     .css('left', from_pos.left + 'px')
            //     .css('top', from_pos.top + 'px');
        }
    });

    setTimeout(() =>
    {
        card_a.remove();
        card_b.remove();
        from_pile.find('.card').show();
        to_pile.find('.card').show();
    }, anim_length);
}

function animate_card_flip(data)
{
    const scale = data.UUID == user_uuid ? 1.0 : +window.getComputedStyle(document.body).getPropertyValue('--player-scale');
    const pile = $(get_pile_selector({
        Pile: PILE_USER,
        OptionalRow: data.Row,
        OptionalColumn: data.Column
    }, data.UUID));
    const position = get_card_position(pile, scale);
    const orig = pile.find('.card');
    const card = $(card_to_html(null, 'animated'));
    const anim_length = 750.0; // ms

    $('#animation-pile').append(card);

    orig.hide();
    card.css('left', position.left + 'px');
    card.css('top', position.top + 'px');
    card.css('transform', `scale(${scale})`);

    $({ Counter: 0.0 }).animate({ Counter: anim_length },
    {
        duration: anim_length,
        step: function(now, fx)
        {
            now /= anim_length;

            if (now > .5)
                now = 1 - now;

            card.css('transform', `scale(${scale}) rotateY(${now * 180}deg)`)
                .css('left', position.left + 'px')
                .css('top', position.top + 'px');
        }
    });

    setTimeout(() => card.attr('data-value', data.Card).html(`<span>${data.Card.Value}</span>`), anim_length / 2);
    setTimeout(() =>
    {
        card.remove();
        orig.show();
    }, anim_length);
}

function animate_column_deletion(data)
{
    // TODO
}

function show_celebration(uuid)
{
    $('.menu-item[data-tab="game"]').click();

    if (uuid == user_uuid)
        show_notification('YOU HAVE WON THE GAME!<br/>CONGRATULATIONS!!');
    else
        show_notification(`${user_to_html(uuid)} has won the game! Can you beat them next time?`);

    setTimeout(() =>
    {
        const conf = uuid == user_uuid ? confetti_ego : confetti_other;

        if (conf != undefined)
        {
            conf({
                particleCount: 1000,
                spread: 90,
                origin: { y: 1 }
            });
            conf({
                particleCount: 800,
                spread: 70,
                angle: -135,
                origin: { y: 0, x: 1 }
            });
            conf({
                particleCount: 800,
                spread: 70,
                angle: -45,
                origin: { y: 0, x: 0 }
            });
        }
    }, 150);
}

function user_to_html(uuid)
{
    const user = user_cache[uuid];
    let name = `{Unknown Player ${uuid.slice(0, 8)}}`;
    let admin = false;
    let server = false;

    if (uuid == '00000000-0000-0000-0000-000000000000')
    {
        name = '[SERVER]';
        server = true;
    }
    else if (user != undefined && user != null)
    {
        name = user.name;
        admin = user.admin;
    }

    return `<span class="player-name" contenteditable="false" data-uuid="${uuid}" ${admin ? 'data-admin' : server ? 'data-server' : ''} ${uuid == user_uuid ? 'data-is-me' : ''}>${name}</span>`;
}

function update_chat_messages()
{
    let html = '';
    let i = 0;

    for (const message of chat_messages_backlog)
    {
        const content = message.Content.replace(UUID_REGEX, m => user_to_html(m.slice(2, -2)));
        let chained = false;

        if (i < chat_messages_backlog.length - 1 && message.UUID == chat_messages_backlog[i + 1].UUID)
        {
            const tdiff_ms = Math.abs(new Date(chat_messages_backlog[i + 1].Time) - new Date(message.Time));

            chained = tdiff_ms / 60000.0 < 20;
        }

        html += `
            <div class="message ${message.UUID == user_uuid ? 'outgoing' : ''} ${chained ? 'chained' : ''}">
                <div class="message-content">${content}</div>
                ${chained ? '' : `
                <div class="message-meta">
                    <span class="message-sender">${user_to_html(message.UUID)}</span>
                    &nbsp;
                    <div class="message-time">${message.Time.slice(0, 19).replace('T', ', ')}</div>
                </div>
                `}
            </div>
        `;
        ++i;
    }

    $('#chat-message-list').html(html);
}

/* 'pile' has the values 'user-x-x', 'draw', 'discard', or 'current'. */
function card_to_html(card, pile)
{
    if (card == undefined)
        card = null;

    if (card != null && card.hasOwnProperty('Value'))
        card = card.Value;

    return `<div class="card" data-value="${card}" data-pile="${pile}">${card == null ? '' : `<span>${card}</span>`}</div>`;
};

function update_game_field(data)
{
    confetti_other = undefined;

    const cls_dragging = 'drag-active';
    const cls_dropped = 'dropped';
    const cls_dragover = 'drag-over';
    const cls_dropallowed = 'drop-allowed';
    const cls_dragallowed = 'drag-allowed';
    const cls_clickable = 'click-allowed';

    $('.card,.pile').removeClass(cls_dragging)
                    .removeClass(cls_dropped)
                    .removeClass(cls_dragover)
                    .removeClass(cls_dropallowed)
                    .removeClass(cls_dragallowed)
                    .removeClass(cls_clickable);
    $('#game-player-count, #draw-pile, #discard-pile, #game-state-text, #players, #ego-player, #instructions, #final-round-warning').html('');
    $('#game-leave, #game-join, #admin-start-game, #admin-stop-game, #admin-reset-game').addClass('hidden');

    if (data == null || data == undefined)
        return;

    const divs = [];
    let index = 0;
    let joined = false;

    for (const player of data.Players)
    {
        const you = player.UUID == user_uuid;
        const is_final = player.UUID == data.FinalRoundInitiator;
        let card_index = 0;
        let card_grid = '';
        let null_cards = 0;

        for (const card of player.Cards)
        {
            const r = Math.floor(card_index / player.Columns);
            const c = card_index % player.Columns;
            const pile = `user-${r}-${c}`;

            if (c == 0)
                card_grid += '<tr>';

            card_grid += `<td class="pile" data-row="${r}" data-col="${c}" data-pile="${pile}">${card_to_html(card, pile)}</td>`;

            if ((card_index + 1) % player.Columns == 0)
            {
                if (initial_board_size != undefined)
                    for (var i = c + 1; i < initial_board_size.columns; ++i)
                        card_grid += `<td class="pile virtual" data-row="${r}" data-col="${i}"></td>`;

                card_grid += '</tr>';
            }

            if (card == null)
                ++null_cards;

            ++card_index;
        }

        if (initial_board_size != undefined)
            for (var j = player.Rows; j < initial_board_size.rows; ++j)
            {
                card_grid += '<tr>';

                for (var i = 0; i < initial_board_size.columns; ++i)
                    card_grid += `<td class="pile virtual" data-row="${j}" data-col="${i}"></td>`;

                card_grid += '</tr>';
            }

        const user_html = user_to_html(player.UUID);
        const user_name = $(user_html).text().trim();
        const is_current = index == data.CurrentPlayer
        const html = `
            <div class="player ${is_current ? 'current' : ''} ${is_final ? 'final' : ''}"
                 data-uuid="${player.UUID}"
                 data-name="${user_name}">
                ${player.LeaderBoardIndex == 0 && !you ? '<canvas id="confetti-other"></canvas>' : ''}
                <div class="player-cards">
                    <table class="player-grid">
                        ${card_grid}
                    </table>
                    <div class="player-side">
                        ${data.Players.length < 2 ? '<div class="player-rank" data-rank="-1">&nbsp;</div>' :
                        `<div class="player-rank" data-rank="${player.LeaderBoardIndex + 1}">
                            ${player.LeaderBoardIndex == 0 ? '1st' :
                              player.LeaderBoardIndex == 1 ? '2st' :
                              player.LeaderBoardIndex == 2 ? '3rd' : '&nbsp;'}
                        </div>`}
                        <div ${you ? 'id="currently-drawn"' : ''}
                             class="pile"
                             data-annotation="currently drawn"
                             data-pile="current">${player.DrawnCard == null ? '' : card_to_html(player.DrawnCard, 'current')}</div>
                        <div class="player-rank" data-rank="-1">&nbsp;</div>
                    </div>
                </div>
                <div class="player-footer">
                    ${user_html}
                    &nbsp;
                    Points: ${player.Points},
                    Rank: ${1 + player.LeaderBoardIndex}
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

            if (data.State == GAMESTATE_RUNNING && null_cards < 2)
                $('#final-round-warning').html(`
                    <b>NOTE:</b>
                    If your current move initiates the final round (i.e. you are the first to have uncovered all cards),
                    an you loose the game, your point score will be doubled.
                `);
        }
        else
            divs.push(`<div class="other-player">${html}</div>`);

        ++index;
    }

    $('#players').html(divs.join('\n'));
    $('#game-player-count').html(data.Players.length);
    $('#draw-pile').html(data.DrawPileSize > 0 ? card_to_html(null, 'draw') : '');
    $('#discard-pile').html(data.DiscardPileSize > 0 ? card_to_html(data.DiscardCard, 'discard') : '');

    confetti_other = confetti.create($('#confetti-other')[0], { resize: true });

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
                Use the <kbd>Join Game</kbd>-button to join the game.
            </p>
        `);
    }

    if (data.State != GAMESTATE_STOPPED)
        $(data.State == GAMESTATE_FINISHED ? '#admin-reset-game' : '#admin-stop-game').removeClass('hidden');
    else if (data.Players.length > 1)
        $('#admin-start-game').removeClass('hidden');

    if (data.CurrentPlayer >= 0 && data.CurrentPlayer < data.Players.length)
        if (data.Players[data.CurrentPlayer].UUID != user_uuid)
        {
            const next_uuid = data.Players[(data.CurrentPlayer + 1) % data.Players.length].UUID;
            const next = next_uuid == user_uuid ? 'You' : user_to_html(next_uuid);

            $('#instructions').html(`It is currently ${user_to_html(data.Players[data.CurrentPlayer].UUID)}'s turn to play. ${next} will be the next player to play.`);
        }
        else
        {
            let enable_drag_and_drop = true;

            $('.card').removeClass(cls_dragallowed);
            $('.pile').removeClass(cls_dropallowed);

            if (data.WaitingFor == WAITINGFOR_DRAW)
            {
                $('.ego-side .card[data-pile="draw"],.ego-side .card[data-pile="discard"]').addClass(cls_dragallowed);
                $('.ego-side .pile[data-pile="current"]').addClass(cls_dropallowed);
                $('#instructions').html('Draw a card from either the discard or draw pile and drop it into your "currently drawn"-slot.');
            }
            else if (data.WaitingFor == WAITINGFOR_PLAY)
            {
                $('.ego-side .card[data-pile="current"]').addClass(cls_dragallowed);
                $('.ego-side .pile[data-pile="discard"],.pile[data-pile*="user"]').addClass(cls_dropallowed);
                $('#instructions').html(`
                    You can either discard your currently drawn card or you can swap the drawn card with any of your own cards.
                    Both actions can be performed by simply dropping the currently drawn card onto the desired slot.
                    Note that you must uncover one of your own cards should you decide to discard the currently drawn card.
                `);
            }
            else if (data.WaitingFor == WAITINGFOR_DISCARD)
            {
                $('.ego-side .card[data-pile="current"]').addClass(cls_dragallowed);
                $('.ego-side .pile[data-pile="discard"]').addClass(cls_dropallowed);
                $('#instructions').html('Discard the swapped card by dropping it onto the discard pile.');
            }
            else
                enable_drag_and_drop = false;

            if (data.WaitingFor == WAITINGFOR_UNCOVER)
            {
                const cards = $('.ego-side .card[data-pile*="user"][data-value="null"]');

                cards.addClass(cls_clickable);
                cards.click(function()
                {
                    const pile = $(this).parent();

                    server_send_query(
                        TYPE_GAME_UNCOVER,
                        { Row: +pile.attr('data-row'), Column: +pile.attr('data-col') },
                        (_, d) => {
                            if (d.Success)
                                cards.removeClass(cls_clickable);
                        }
                    );
                });

                $('#instructions').html(`You are expected to uncover one of your cards. Simply tap the card to turn it over.`);
            }

            if (enable_drag_and_drop)
            {
                $(`.ego-side .card.${cls_dragallowed}`).draggable({
                    start: (e, ui) =>
                    {
                        $(e.target).addClass(cls_dragging);
                        $(`.pile.${cls_dropallowed}`).addClass(cls_dragging);
                    },
                    stop: (e, ui) =>
                    {
                        const card = $(e.target);
                        const pile = $(`.pile.${cls_dropped}`);
                        const valid = pile.hasClass(cls_dropallowed);
                        const from = card.attr('data-pile');
                        const to = pile.attr('data-pile');

                        pile.removeClass(cls_dropped);
                        card.removeClass(cls_dragging);
                        $(`.pile.${cls_dragover}`).removeClass(cls_dragging);

                        const revert = () => card.animate({
                            left: '0px',
                            top: '0px',
                        });

                        if (valid)
                            if (to == 'current' && (from == 'draw' || from == 'discard'))
                                return server_send_query(
                                    TYPE_GAME_DRAW,
                                    { Pile: from == 'draw' ? PILE_DRAW : PILE_DISCARD },
                                    (_, d) => {
                                        if (!d.Success)
                                            revert();
                                    }
                                );
                            else if (from == 'current' && to == 'discard')
                                return server_send_query(TYPE_GAME_DISCARD, { }, (_, d) => {
                                    if (!d.Success)
                                        revert();
                                });
                            else if (from == 'current' && to.startsWith('user'))
                                return server_send_query(
                                    TYPE_GAME_SWAP,
                                    {
                                        Row: +pile.attr('data-row'),
                                        Column: +pile.attr('data-col')
                                    },
                                    (_, d) => {
                                        if (!d.Success)
                                            revert();
                                    }
                                );

                        revert();
                    }
                });
                $('.ego-side .pile').droppable({
                    drop: function(e, ui)
                    {
                        $(`.pile`).removeClass(cls_dragover);
                        $(e.target).addClass(cls_dropped);
                    },
                    accept: function(e) { return $(this).hasClass(cls_dropallowed); },
                    over: function(e, _) { $(e.target).addClass(cls_dragover); },
                    out: function(e, _) { $(e.target).removeClass(cls_dragover); }
                });
                $('*:not(.pile)').droppable({ accept: () => false });
            }
        }
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
    $('#login-container').addClass('hidden');
    $('#username-container').removeClass('hidden');
    $('#username-error').html('');
    $('#username-input').val(user_name).focus().select();
    $('#username-container .first-only, #username-container .veteran').show();

    if (first_time)
    {
        $('#login-container, #username-container').addClass('first-time');
        $('#username-container .veteran').hide();
    }
    else
    {
        $('#login-container, #username-container').removeClass('first-time');
        $('#username-container .first-only').hide();
    }
}

const preventDefault = e => e.preventDefault();

function update_server_and_player_info()
{
    let html = '<table>';
    let sorted = [];

    for (const uuid in user_cache)
        sorted.push({
            name: user_cache[uuid].name,
            admin: user_cache[uuid].admin,
            server: user_cache[uuid].server,
            game: user_cache[uuid].game,
            uuid: uuid
        });

    sorted.sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
    sorted.sort((a, b) => a.admin == b.admin ? 0 : a.admin ? -1 : 1);
    sorted.sort((a, b) => a.server ? -1 : a.server == b.server ? 0 : 1);

    const admin_view = $('#main-container').hasClass('admin');

    for (const user of sorted)
        if (user.server)
            html += `
                <tr class="separator">
                    <td class="level server"></td>
                    <td>[SERVER]</td>
                    ${admin_view ? '<td></td><td></td>' : ''}
                </tr>
            `;
        else
            html += `
                <tr ${!admin_view ? 'class="separator"' : ''}>
                    <td class="level ${user.admin ? 'admin' : ''}"></td>
                    <td>
                        ${user.name}
                        ${user.uuid == user_uuid ? '<span>You</span>' : ''}
                    </td>
                    ${admin_view ? `
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
                </tr>
                <tr class="separator">
                    <td colspan="2">{${user.uuid}}</td>
                    <td class="admin-only">
                        <button class="admin-confetti" data-uuid="${user.uuid}">
                            <span class="emoji">✨</span>
                            Confetti
                            <span class="emoji">✨</span>
                        </button>
                    </td>
                    <td class="admin-only">
                        <button class="admin-make-${user.admin ? 'regular' : 'admin'}" data-uuid="${user.uuid}">
                            Make ${user.admin ? 'regular' : 'admin'} user
                        </button>
                    </td>
                ` : ''}
                </tr>
            `;

    html += '</html>';

    $('#player-count').html(sorted.length);
    $('#player-list').html(html);

    const reg_handler = (selector, type) => $(selector).click(e => server_send_command(type, { UUID: $(e.target).attr('data-uuid') }));

    reg_handler('.admin-kick-from-server[data-uuid]', TYPE_KICK_PLAYER);
    reg_handler('.admin-kick-from-game[data-uuid]', TYPE_REMOVE_GAME_PLAYER);
    reg_handler('.admin-make-regular[data-uuid]', TYPE_MAKE_REGULAR);
    reg_handler('.admin-make-admin[data-uuid]', TYPE_MAKE_ADMIN);
    reg_handler('.admin-confetti[data-uuid]', TYPE_REQ_WIN_ANIM);
}

function upate_user_info(uuid)
{
    server_send_query(TYPE_PLAYER_INFO_REQUEST, { UUID: uuid }, (_, response) =>
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
                server: response.IsServer,
                game: response.IsInGame
            };

            if (uuid == user_uuid)
            {
                if (response.IsAdmin)
                    $('#main-container').addClass('admin');
                else
                    $('#main-container').removeClass('admin');

                if (!$('#user-name').is(':focus'))
                    $('#user-name').val(response.Name);
            }
        }

        update_chat_messages();
        update_server_and_player_info();
    });
}

function change_username_req(name, callback)
{
    server_send_query(TYPE_NAME_REQUEST, {Name: name}, (_, d) => callback(d));
}


$('#login-string').on('input change paste keyup', on_login_input_changed);

$('#login-string').keypress(e =>
{
    if (e.keyCode == 13)
        $('#login-start').click();
});

$('#login-start').click(function()
{
    const string = $('#login-string').val();
    const target = decode_connection_string(string);
    const share_url = `${current_url.origin}${current_url.pathname}?code=${string}`;

    window.localStorage.setItem(STORAGE_CONN_STRING, string);

    $('#share-url').attr('href', share_url).html(share_url);
    $('.qr-wrapper').html('<div id="qr-code"></div>');

    new QRCode("qr-code", {
        text: share_url,
        width: 300,
        height: 300,
        colorDark : 'black',
        colorLight : 'transparent',
        correctLevel : QRCode.CorrectLevel.H
    });

    if (socket === undefined && target !== undefined)
    {
        const secure = current_url.protocol.indexOf('s') != -1;
        const cert_url = `${current_url.protocol}//${target.address}:${target.wss}/`;
        const http_url = `http://${current_url.hostname}:80${current_url.pathname}?code=${string}`;

        if (secure)
            $('.unblock-hint').show();
        else
            $('.unblock-hint').hide();

        $('#login-form').addClass('loading');
        $('.unblock-url').html(cert_url).attr('href', cert_url);
        $('.insecure-url').html(http_url).attr('href', http_url);

        setTimeout(function()
        {
            incoming_queue = new Array();
            outgoing_queue = new Array();

            try
            {
                socket = new WebSocket(`${secure ? 'wss' : 'ws'}://${target.address}:${secure ? target.wss : target.ws}`);
                socket.onclose = socket_close;
                socket.onopen = socket_open;
                socket.onmessage = e => incoming_queue.push(JSON.parse(e.data));
                socket.onerror = socket_error;
            }
            catch (e)
            {
                socket_close();
                $('#login-form').addClass('failed');
            }
        }, 350);
    }
});

$('#unblock-cerificate').click(() => $('#unblock-container').removeClass('hidden'));

$('#close-unblock-container').click(() => $('#unblock-container').addClass('hidden'));

$('#login-failed-dismiss').click(() => $('#login-form').removeClass('failed'));

$('#notification-close').click(hide_notification);

$('#username-input').keypress(e =>
{
    if (e.keyCode == 13)
        $('#username-apply').click();
});

$('#username-apply').click(() =>
{
    $('#username-error').html('');

    change_username_req($('#username-input').val(), response =>
    {
        if (response.Success)
        {
            user_name = $('#username-input').val();
            window.localStorage.setItem(STORAGE_USER_NAME, user_name);

            $('body').addClass('ready');
            $('#game-container').removeClass('hidden');
            $('#username-container').addClass('hidden');
            $('#user-name').val(user_name);

            if ((window.location.hash || '').length > 2)
                $(`.menu-bar .menu-item[data-tab="${window.location.hash.slice(1)}"]`).click();
            else
                window.location.hash = '#game';
        }
        else
            $('#username-error').html(response.Message);
    });
});

$('#username-generate').click(() => $('#username-input').val(generate_random_name()).focus().select());

$('#user-name').keypress(e =>
{
    $('#user-name').html('');

    if (e.keyCode == 13)
        change_username_req($('#user-name').val(), response =>
        {
            if (response.Success)
            {
                user_name = $('#user-name').val();
                window.localStorage.setItem(STORAGE_USER_NAME, user_name);

                $('#username-input').val(user_name);
            }
            else
                $('#user-error').html(response.Message);
        });
});

$('#instruction-selector .tab-selector .tab[data-tab]').click(function()
{
    const elem = $(this);
    const tab = elem.attr('data-tab');
    const content = $(`#instruction-selector .tab-container .tab[data-tab="${tab}"]`);

    elem.addClass('active');
    elem.siblings().removeClass('active');
    content.siblings().addClass('hidden');
    content.removeClass('hidden');
});

$('.menu-bar .menu-item[data-tab]').click(function()
{
    const elem = $(this);
    const tab = elem.attr('data-tab');
    const content = $(`.menu-content-holder .menu-content[data-tab="${tab}"]`);

    $('#main-container').attr('data-tab', tab);

    elem.addClass('active');
    elem.siblings().removeClass('active');
    content.siblings().addClass('hidden');
    content.removeClass('hidden');
    window.location.hash = '#' + tab;
});

$('#logout-button').click(() => socket.close());

$('#reset-logout-button').click(function()
{
    $('#logout-button').click();

    window.localStorage.clear();
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
    // TODO : leave confirmation

    $('#game-leave').addClass('hidden');

    server_send_command(TYPE_LEAVE_REQUEST, { });
});

$('#admin-start-game').click(() => server_send_command(TYPE_GAME_START, { }));

$('#admin-stop-game').click(() => server_send_command(TYPE_GAME_STOP, { }));

$('#admin-reset-game').click(() => server_send_command(TYPE_GAME_RESET, { }));

$('#shut-down-server').click(() => server_send_command(TYPE_SERVER_STOP, { }));

$('#change-init-board-size').click(() => server_send_query(TYPE_BOARD_SIZE, {
    Columns: +$('#init-board-cols').val(),
    Rows: +$('#init-board-rows').val()
}, (_, data) => {
    if (!data.Success)
        show_notification(data.Message, false);
}));

$(window).on('hashchange', () =>
{
    const hash = (window.location.hash || '#').slice(1);

    if (hash.length > 0 && $('#main-cotainer').attr('data-tab') != hash)
        $(`.menu-bar .menu-item[data-tab="${hash}"]`).click();
});


const chat_input_box = $('#chat-input .input');
let chat_input_range = undefined;

function get_chat_text_selection()
{
    chat_input_box.focus();

    const range = window.getSelection().getRangeAt(0);
    const pre_caret = range.cloneRange();

    pre_caret.selectNodeContents(chat_input_box[0]);
    pre_caret.setEnd(range.startContainer, range.startOffset);

    const start = pre_caret.toString().length;

    pre_caret.setEnd(range.endContainer, range.endOffset);

    const end = pre_caret.toString().length;

    return { start: start, end: end, range: range };
}

function insert_chat_text_at_selection(content)
{
    chat_input_box.focus();

    const range = window.getSelection().getRangeAt(0);

    if (range.commonAncestorContainer == chat_input_box[0] || range.commonAncestorContainer.parentNode == chat_input_box[0])
    {
        const node = document.createTextNode(content);

        range.deleteContents();
        range.insertNode(node);
        range.selectNodeContents(node);
        range.collapse();
        chat_input_box.trigger('change');
    }
}

function restore_selection()
{
    if (chat_input_range)
    {
        const selection = window.getSelection();

        selection.removeAllRanges();
        selection.addRange(chat_input_range);

        return chat_input_range;
    }
}

chat_input_box.on('focus', () =>
{
    restore_selection();
    chat_input_box.data('before', chat_input_box.html());
}).on('paste', e =>
{
    let text = undefined;

    if (e.clipboardData || e.originalEvent.clipboardData)
        text = (e.originalEvent || e).clipboardData.getData('text/plain');
    else if (window.clipboardData)
        text = window.clipboardData.getData('Text');

    if (text != undefined)
    {
        e.preventDefault();
        insert_chat_text_at_selection(text);
    }
    else
        chat_input_box.trigger('change');
}).on('blur keyup input', function()
{
    if (chat_input_box.data('before') !== chat_input_box.html())
    {
        chat_input_box.data('before', chat_input_box.html());
        chat_input_box.trigger('change');
    }
}).on('selectionchange blur change', () => chat_input_range = window.getSelection().getRangeAt(0))
.keypress(e =>
{
    if (e.keyCode == 13)
        if (e.shiftKey)
            insert_chat_text_at_selection('\n');
        else
            $('#chat-send').click();
}).change(() =>
{
    const text = chat_input_box.text().trim();

    if (text.includes('{{'))
    {
        const { start, end, range } = get_chat_text_selection();
        let text_index = 0;

        for (let content of chat_input_box.contents())
            if (content.nodeType == Node.TEXT_NODE)
            {
                content = $(content);

                const existing = content.text();
                const html = existing.replace(UUID_REGEX, m => user_to_html(m.slice(2, -2)));

                if (html != existing)
                {
                    const replacement = content.replaceWith(html);

                    if ((start >= text_index && start < text_index + existing.length) ||
                        (end >= text_index && end < text_index + existing.length))
                    {
                        range.selectNodeContents(replacement[0]);
                        range.collapse(false);
                    }
                }

                text_index += existing.length;
            }
            else
                text_index += $(content).text().length;
    }

    if (text.length > 0)
        $('#chat-send').removeClass('hidden');
    else
        $('#chat-send').addClass('hidden');
});

$('#chat-mention').click(() =>
{
    let html = '';

    for (const uuid in user_cache)
        html += `<div class="mention" data-uuid="${uuid}">${user_to_html(uuid)}</div>`;

    $('#chat-mention-menu').html(html).removeClass('hidden');
    $('#chat-mention-menu .mention[data-uuid]').click(e =>
    {
        const uuid = $(e.target).attr('data-uuid');

        insert_chat_text_at_selection(`{{${uuid}}}`);
        $('#chat-mention-menu').addClass('hidden');
    });

    return false;
});

window.addEventListener('click', e =>
{
    const menu = $('#chat-mention-menu');

    if (!menu[0].contains(e.target))
        menu.addClass('hidden');
});

$('#chat-send').click(() =>
{
    let message = '';

    for (const content of chat_input_box.contents())
        if (content.nodeType == Node.TEXT_NODE)
            message += content.nodeValue;
        else if ($(content).hasClass('player-name'))
            message += `{{${$(content).attr('data-uuid')}}}`;

    server_send_query(TYPE_SEND_CHAT, { Content: message }, (_, d) => {
        if (d.Success)
            chat_input_box.html('');
    });
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
        text: 'SKHEIJO game on ' + current_url.hostname,
        title: 'SKHEIJO game on ' + current_url.hostname
    }));




