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

// 端末の機能（画面を消さない・通知音・ファイルの保存）
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

    download(fileName, text) {
        const blob = new Blob([text], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = fileName;
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    },
};
