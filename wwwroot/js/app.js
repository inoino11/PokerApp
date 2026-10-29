// 起動中の全画面表示
window.appLoading = {
    hide() {
        const el = document.getElementById('app-loading');
        if (!el) return;
        el.classList.add('done');
        setTimeout(() => el.remove(), 400);
    },
    fail() {
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
        try {
            const ctx = this._audio ??= new (window.AudioContext || window.webkitAudioContext)();
            const beep = (t, f) => {
                const o = ctx.createOscillator();
                const g = ctx.createGain();
                o.frequency.value = f;
                g.gain.setValueAtTime(0.25, ctx.currentTime + t);
                g.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + t + 0.25);
                o.connect(g).connect(ctx.destination);
                o.start(ctx.currentTime + t);
                o.stop(ctx.currentTime + t + 0.3);
            };
            beep(0, 880); beep(0.3, 1175);
        } catch { }
    },

    scrollToActive() {
        document.querySelector('.row-to-act')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    },
};
