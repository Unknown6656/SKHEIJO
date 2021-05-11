const UNSUPPORTED_BROWSERS = {
    Chrome: 66,
    Firefox: 60,
    // IE: 11,   <-- uncommented = no IE support!
    Edge: 17,
    Opera: 53,
    Safari: 11,
    Samsung: 7,
};


class Browser
{
    constructor()
    {
        this.user_agent = navigator.userAgent;

        let tem, M = this.user_agent.match(/(opera|chrome|safari|firefox|msie|trident(?=\/))\/?\s*(\d+)/i) || [];

        if (/trident/i.test(M[1]))
        {
            tem = /\brv[ :]+(\d+)/g.exec(this.user_agent) || [];
            this.browser = { name: "IE", version: tem[1] || "" };

            return;
        }

        if (M[1] === "Chrome")
        {
            tem = this.user_agent.match(/\b(OPR|Edge)\/(\d+)/);

            if (tem != null)
            {
                this.browser = { name: tem[1].replace("OPR", "Opera"), version: tem[2] };

                return;
            }
        }

        M = M[2] ? [M[1], M[2]] : [navigator.appName, navigator.appVersion, "-?"];

        if ((tem = this.user_agent.match(/version\/(\d+)/i)) != null)
            M.splice(1, 1, tem[1]);

        this.browser = { name: M[0], version: M[1] };
    }

    get isIE() { return this.browser.name === 'IE'; }

    get isEdge() { return this.browser.name === 'Edge'; }

    get isMicrosoft() { return this.isIE || this.isEdge; }

    get isFirefox() { return this.browser.name === 'Firefox'; }

    get isChrome() { return this.browser.name === 'Chrome'; }

    get isSafari() { return this.browser.name === 'Safari'; }

    get isAndroid() { return /Android/i.test(this.user_agent); }

    get isBlackBerry() { return /BlackBerry/i.test(this.user_agent); }

    get isWindowsMobile() { return /IEMobile/i.test(this.user_agent); }

    get isIOS() { return /iPhone|iPad|iPod/i.test(this.user_agent); }

    get isMobile() { return (this.isAndroid || this.isBlackBerry || this.isWindowsMobile || this.isIOS); }

    isSupported() {
        return UNSUPPORTED_BROWSERS.hasOwnProperty(this.browser.name)
            && +this.browser.version >= UNSUPPORTED_BROWSERS[this.browser.name];
    }

    toString() { return `${this.browser.name} ${this.browser.version}`; }
}

Browser.current = new Browser();
