/* THIS SCRIPT HAS TO BE ES5 COMPATIBLE */

var browser = {
    user_agent: navigator.userAgent,
    supported: false,
    name: null,
    version: null
}
var UNSUPPORTED_BROWSERS = {
    Chrome: 66,
    Firefox: 60,
    // IE: 11,   <-- uncommented = no IE support!
    Edge: 17,
    Opera: 53,
    Safari: 11,
    Samsung: 7,
};

{
    var tem, M = browser.user_agent.match(/(opera|chrome|safari|firefox|msie|trident(?=\/))\/?\s*(\d+)/i) || [];

    if (/trident/i.test(M[1]))
    {
        tem = /\brv[ :]+(\d+)/g.exec(browser.user_agent) || [];
        browser.name = "IE";
        browser.version = tem[1] || "";
    }
    else if (M[1] === "Chrome")
    {
        tem = browser.user_agent.match(/\b(OPR|Edge)\/(\d+)/);

        if (tem != null)
        {
            browser.name = tem[1].replace("OPR", "Opera");
            browser.version = tem[2];
        }
    }

    if (browser.name == null)
    {
        M = M[2] ? [M[1], M[2]] : [navigator.appName, navigator.appVersion, "-?"];

        if ((tem = browser.user_agent.match(/version\/(\d+)/i)) != null)
            M.splice(1, 1, tem[1]);

        browser.name = M[0];
        browser.version = M[1];
    }
}

browser.isIE = browser.name === 'IE';
browser.isEdge = browser.name === 'Edge';
browser.isMicrosoft = browser.isIE || browser.isEdge;
browser.isFirefox = browser.name === 'Firefox';
browser.isChrome = browser.name === 'Chrome';
browser.isSafari = browser.name === 'Safari';
browser.isAndroid = /Android/i.test(browser.user_agent);
browser.isBlackBerry = /BlackBerry/i.test(browser.user_agent);
browser.isWindowsMobile = /IEMobile/i.test(browser.user_agent);
browser.isIOS = /iPhone|iPad|iPod/i.test(browser.user_agent);
browser.isMobile = browser.isAndroid || browser.isBlackBerry || browser.isWindowsMobile || browser.isIOS;
browser.isSupported = UNSUPPORTED_BROWSERS.hasOwnProperty(browser.name) && +browser.version >= UNSUPPORTED_BROWSERS[browser.name];
browser.string = browser.name + ' ' + browser.version;


{
    var show_warning = false;
    var min_screen_size = parseInt(window.getComputedStyle(document.body).getPropertyValue('--min-width').replace('px', ''));

    $('#min-width').html(min_screen_size);

    if (!browser.isSupported)
    {
        var supported = '';

        for (var b in UNSUPPORTED_BROWSERS)
            supported += '<tr><td>' + b + ':</td><td>&gt; ' + UNSUPPORTED_BROWSERS[b] + '</td></tr>';

        $('#usage-warning [data-warning="version"]').removeClass('hidden');
        $('#current-browser').html(browser.string);
        $('#supported-browsers').html(supported);

        show_warning = true;
    }

    if (min_screen_size > window.innerHeight || min_screen_size > window.innerWidth)
    {
        var viewport = $("#viewport");

        if (screen.width < min_screen_size)
            viewport.attr('content', viewport.attr('content') + ', height=' + min_screen_size);

        $('#usage-warning [data-warning="size"]').removeClass('hidden');

        show_warning = true;
    }

    if (show_warning)
        $('#usage-warning-dismiss').removeClass('hidden').click(function()
        {
            $('#usage-container').remove();
            $('#login-container').removeClass('hidden');

            load_page();
        });
    else
    {
        $('#usage-container').remove();
        $('#login-container').removeClass('hidden');

        load_page();
    }
}

function load_page()
{
    var interval = setInterval(function()
    {
        if (on_page_loaded != undefined)
        {
            clearInterval(interval);
            on_page_loaded();
        }
    }, 200);
}
