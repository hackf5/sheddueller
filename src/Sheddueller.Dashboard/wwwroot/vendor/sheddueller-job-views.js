(function () {
    "use strict";

    window.shedduellerJobViews = {
        load: function (key) {
            const value = window.localStorage.getItem(key);
            return value === null ? null : JSON.parse(value);
        },
        save: function (key, value) {
            window.localStorage.setItem(key, JSON.stringify(value));
        }
    };
})();
