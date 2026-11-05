// 起動中の全画面表示
window.appLoading = {
    // チップが宙にあるうちに消すと途切れて見えるので、着地を待ってから消す（待つのは長くても 1 秒ほど）。
    // 時間の区切りは app.css の app-chip-flip（1.8 秒、15% で跳ね、75% で着地）に合わせる
    hide() {
        const el = document.getElementById('app-loading');
        if (!el) return;
        const cycle = 1800, lift = cycle * 0.15, land = cycle * 0.75;
        const anim = el.querySelector('.app-loading-chip')?.getAnimations?.()[0];
        const t = anim ? Number(anim.currentTime) % cycle : 0;
        const wait = t > lift && t < land ? land - t : 0;
        setTimeout(() => {
            el.classList.add('done');
            setTimeout(() => el.remove(), 400);
        }, wait);
    },
    fail() {
        document.getElementById('app-loading')?.classList.add('failed');
        document.getElementById('app-loading-error')?.removeAttribute('hidden');
    },
    // Service Worker とキャッシュを消して読み直す（古いファイルが残って起動できないときの逃げ道）
    async recover() {
        try {
            const regs = await navigator.serviceWorker?.getRegistrations() ?? [];
            await Promise.all(regs.map(r => r.unregister()));
            const keys = await caches.keys();
            await Promise.all(keys.map(k => caches.delete(k)));
        } finally {
            location.reload();
        }
    },
};

// 席替えのドラッグ。.seat-handle をつかんだ行を指に追わせ、ほかの行をずらして入る場所を見せる。
// DOM の並びは Blazor が持っているので、ここでは transform で見た目だけ動かし、離したときに
// C# の OnSeatDropped(from, to) を呼んで並びを確定させる。
window.seatDrag = {
    _dotnet: null,

    register(dotnet) {
        this._dotnet = dotnet;
        if (this._listening) return;
        this._listening = true;
        document.addEventListener('pointerdown', e => this._start(e));
    },

    unregister() {
        this._dotnet = null;
    },

    _start(e) {
        const handle = e.target.closest?.('.seat-handle');
        if (!handle || !this._dotnet) return;
        const list = handle.closest('.player-list');
        const row = handle.closest('.player-row');
        const rows = [...list.querySelectorAll('.player-row')];
        const from = rows.indexOf(row);
        const rects = rows.map(r => r.getBoundingClientRect());
        // 隣の行との間隔も含めた 1 行分のずらし幅
        const step = rects.length > 1 ? rects[1].top - rects[0].top : rects[0].height;
        const startY = e.clientY;
        let to = from;

        e.preventDefault();
        handle.setPointerCapture(e.pointerId);
        row.classList.add('row-dragging');

        const move = ev => {
            // 一覧の上端から下端までに収める
            const dy = Math.max(rects[0].top - rects[from].top, Math.min(rects[rects.length - 1].top - rects[from].top, ev.clientY - startY));
            row.style.transform = `translateY(${dy}px)`;
            const center = rects[from].top + rects[from].height / 2 + dy;
            to = rects.filter((r, i) => i !== from && r.top + r.height / 2 < center).length;
            rows.forEach((r, i) => {
                if (i === from) return;
                const shift = from < to && i > from && i <= to ? -step
                    : to < from && i >= to && i < from ? step : 0;
                r.style.transform = shift ? `translateY(${shift}px)` : '';
            });
        };
        const end = () => {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', end);
            handle.removeEventListener('pointercancel', end);
            rows.forEach(r => { r.style.transform = ''; });
            row.classList.remove('row-dragging');
            if (to !== from) this._dotnet?.invokeMethodAsync('OnSeatDropped', from, to);
        };
        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', end);
        handle.addEventListener('pointercancel', end);
    },
};

// 通知（MudBlazor の Snackbar）を、タップかどの向きへのスワイプでも消す。
// 閉じるのは通知の ✖ を JS から押して MudBlazor に任せる（表示中の一覧とずれないように）。
// MudBlazor は描き直すたびに style 属性を書き換えるので、動きは style ではなく Web Animations で付ける
window.snackbarGesture = {
    _drag: null,

    init() {
        document.addEventListener('pointerdown', e => this._down(e));
        document.addEventListener('pointermove', e => this._move(e));
        document.addEventListener('pointerup', e => this._up(e));
        document.addEventListener('pointercancel', () => this._reset());
        // ✖ をタップしたときは、こちらで閉じたあとに本物のクリックも届くので、二重に閉じないよう止める
        window.addEventListener('click', e => {
            if (e.isTrusted && e.target.closest?.('.mud-snackbar[data-closing]')) {
                e.stopImmediatePropagation();
                e.preventDefault();
            }
        }, true);
    },

    _down(e) {
        const el = e.target.closest?.('.mud-snackbar');
        if (!el || el.dataset.closing) return;
        // マウスで動かしたときに、周りの文字が選ばれたりブラウザ自身のドラッグ（pointercancel になる）が始まったりしないように
        e.preventDefault();
        this._reset();
        const anim = el.animate([{ transform: 'none' }], { duration: 0, fill: 'forwards' });
        this._drag = { el, anim, id: e.pointerId, x: e.clientX, y: e.clientY, dx: 0, dy: 0 };
    },

    _move(e) {
        const d = this._drag;
        if (!d || e.pointerId !== d.id) return;
        d.dx = e.clientX - d.x;
        d.dy = e.clientY - d.y;
        d.anim.effect.setKeyframes([this._frame(d.dx, d.dy)]);
    },

    _up(e) {
        const d = this._drag;
        if (!d || e.pointerId !== d.id) return;
        this._drag = null;
        const dist = Math.hypot(d.dx, d.dy);
        if (dist >= 8 && dist < 50) {  // 少し動かしただけなら元の位置に戻す
            d.anim.cancel();
            return;
        }
        if (dist >= 50) {
            const k = 400 / dist;  // 動かした向きへ画面の外まで流す
            d.el.animate([this._frame(d.dx, d.dy), { transform: `translate(${d.dx * k}px, ${d.dy * k}px)`, opacity: 0 }],
                { duration: 150, fill: 'forwards' });
            d.anim.cancel();
        }
        d.el.dataset.closing = '1';
        d.el.querySelector('.mud-snackbar-close-button')?.click();
    },

    _frame(dx, dy) {
        return { transform: `translate(${dx}px, ${dy}px)`, opacity: Math.max(0.3, 1 - Math.hypot(dx, dy) / 200) };
    },

    _reset() {
        this._drag?.anim.cancel();
        this._drag = null;
    },
};
snackbarGesture.init();

// 端末の機能（画面を消さない・通知音・手番へのスクロール）
window.appDevice = {
    _lock: null,
    _wantLock: false,

    // 画面が裏に回ると解除されるので、表に戻ったら取り直す
    async keepAwake() {
        this._wantLock = true;
        if (!('wakeLock' in navigator)) return 'UNSUPPORTED';
        if (!this._listening) {
            this._listening = true;
            document.addEventListener('visibilitychange', () => {
                if (document.visibilityState === 'visible' && this._wantLock) this._acquire();
            });
        }
        return await this._acquire();
    },
    async _acquire() {
        try {
            this._lock = await navigator.wakeLock.request('screen');
            return 'OK';
        } catch (e) {
            return 'ERROR: ' + e.message;
        }
    },

    levelUp() {
        try { navigator.vibrate?.([200, 100, 200]); } catch { }
        this._beep(0, 880);
        this._beep(0.3, 1175);
    },

    // レベルアップの 2 音と聞き分けられるよう、低めの 1 音にする
    oneMinute() {
        try { navigator.vibrate?.([100]); } catch { }
        this._beep(0, 660);
    },

    // iOS はタップの流れの中で作るか再開した AudioContext でないと鳴らさない。裏から戻ると止まる（interrupted）ので、
    // タップのたびに再開しておき、時計の処理から鳴らすときに使えるようにする
    _initAudio() {
        const unlock = () => this._unlock();
        document.addEventListener('pointerdown', unlock, true);
        document.addEventListener('touchend', unlock, true);
    },

    _context() {
        if (!this._audio) {
            // 'ambient' は他のアプリの音と混ぜて鳴らす種類。既定のままだと、流している音楽を止めることがある
            try { if (navigator.audioSession) navigator.audioSession.type = 'ambient'; } catch { }
            this._audio = new (window.AudioContext || window.webkitAudioContext)();
        }
        return this._audio;
    },

    _unlock() {
        try {
            const ctx = this._context();
            if (ctx.state !== 'running') ctx.resume().catch(() => { });
            // 古い iOS は、タップの中で一度何かを鳴らさないと使えるようにならないので、無音を一瞬鳴らす
            if (!this._primed) {
                this._primed = true;
                const src = ctx.createBufferSource();
                src.buffer = ctx.createBuffer(1, 1, 22050);
                src.connect(ctx.destination);
                src.start(0);
            }
        } catch { }
    },

    _beep(t, f) {
        try {
            const ctx = this._context();
            if (ctx.state !== 'running') ctx.resume().catch(() => { });
            const o = ctx.createOscillator();
            const g = ctx.createGain();
            o.frequency.value = f;
            g.gain.setValueAtTime(0.25, ctx.currentTime + t);
            g.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + t + 0.25);
            o.connect(g).connect(ctx.destination);
            o.start(ctx.currentTime + t);
            o.stop(ctx.currentTime + t + 0.3);
        } catch { }
    },

    scrollToActive() {
        document.querySelector('.row-to-act')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    },
};
appDevice._initAudio();
