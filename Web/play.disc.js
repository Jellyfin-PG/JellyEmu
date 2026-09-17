// JellyEmu Play! Disc Image Device Stream Wrapper
// Direct port of DiscImageDevice.ts from upstream alvaro-zamorano/ps2web.
(function(window) {
    'use strict';

    const IO_WORKER_SRC = `
    let file = null, memory = null, ctrlPtr = 0, ctrl = null;
    let reader = null;
    try { reader = new FileReaderSync(); } catch (e) {}

    function view() {
        if (!ctrl || ctrl.buffer !== memory.buffer) ctrl = new Int32Array(memory.buffer, ctrlPtr, 8);
        return ctrl;
    }

    self.onmessage = (e) => {
        const d = e.data;
        file = d.file;
        memory = d.memory;
        ctrlPtr = d.ctrlPtr;
        // Do NOT touch ctrl[0] here: the EE may already have posted a request.
        // Sizes were written by the main thread before the worker was created.
        self.postMessage('ready');
        loop();
    };

    function loop() {
        for (;;) {
            let c = view();
            let s = Atomics.load(c, 0);
            while (s !== 1) {
                Atomics.wait(c, 0, s);
                c = view();
                s = Atomics.load(c, 0);
            }
            const lo = Atomics.load(c, 1) >>> 0, hi = Atomics.load(c, 2) >>> 0;
            const size = Atomics.load(c, 3) >>> 0, dst = Atomics.load(c, 4) >>> 0;
            const pos = lo + hi * 4294967296;
            let n = 0;
            try {
                if (reader) {
                    const buf = reader.readAsArrayBuffer(file.slice(pos, pos + size));
                    n = buf.byteLength;
                    new Uint8Array(memory.buffer, dst, n).set(new Uint8Array(buf));
                } else {
                    n = -1;
                }
            } catch (err) {
                n = -1;
            }
            c = view();
            Atomics.store(c, 5, n);
            Atomics.store(c, 0, 2);
            Atomics.notify(c, 0);
        }
    }
    `;

    class PlayDiscImageDevice {
        constructor(module) {
            this.module = module;
            this.doneFlag = false;
            this.file = null;
            this.worker = null;
            this.ctrlPtr = 0;
        }

        // Legacy path (main thread, polled from the EE). Kept as fallback.
        read(dstPtr, offset, size) {
            if (!this.file) {
                throw new Error("No file set.");
            }
            this.doneFlag = false;
            const subsection = this.file.slice(offset, offset + size);
            subsection.arrayBuffer().then((value) => {
                if (this.module && this.module.HEAPU8) {
                    this.module.HEAPU8.set(new Uint8Array(value), dstPtr);
                }
                this.doneFlag = true;
            }).catch((err) => {
                console.error('[JellyEmu Play!] Disc read error:', err);
                this.doneFlag = true;
            });
        }

        getFileSize() {
            if (!this.file) {
                throw new Error("No file set.");
            }
            return this.file.size;
        }

        isDone() {
            return this.doneFlag;
        }

        // PS2WEB(15): 0 = use the legacy path.
        getCtrlPtr() {
            return this.ctrlPtr;
        }

        setFile(file) {
            this.file = file;
            this.stopWorker();
            try {
                const mem = this.module.wasmMemory;
                const disabled = /[?&]discproxy=1/.test(window.location.search);
                if (!disabled && mem && typeof SharedArrayBuffer !== 'undefined' && mem.buffer instanceof SharedArrayBuffer && typeof this.module.getDiscCtrlPtr === 'function') {
                    const ptr = this.module.getDiscCtrlPtr();
                    if (ptr) {
                        const c = new Int32Array(mem.buffer, ptr, 8);
                        c.fill(0);
                        c[6] = file.size % 4294967296;
                        c[7] = Math.floor(file.size / 4294967296);
                        const url = URL.createObjectURL(new Blob([IO_WORKER_SRC], { type: 'text/javascript' }));
                        const w = new Worker(url);
                        w.onerror = (e) => { console.error('PS2WEB(15): IO worker error', e); };
                        w.postMessage({ file, memory: mem, ctrlPtr: ptr });
                        this.worker = w;
                        this.ctrlPtr = ptr;
                        console.log('PS2WEB(15): disc IO worker armed (ctrl=' + ptr + ', size=' + file.size + ')');
                    }
                } else {
                    console.log('PS2WEB(15): disc IO worker unavailable, legacy main-thread reads');
                }
            } catch (e) {
                console.error('PS2WEB(15): IO worker setup failed, legacy reads', e);
                this.ctrlPtr = 0;
            }
        }

        stopWorker() {
            if (this.worker) {
                try { this.worker.terminate(); } catch (e) {}
            }
            this.worker = null;
            this.ctrlPtr = 0;
        }
    }

    window.PlayDiscImageDevice = PlayDiscImageDevice;
})(window);
