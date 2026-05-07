/**
 * JellyEmu XR Module (VR / AR)
 *
 * Depends on:
 *   - Three.js already loaded (window.THREE)
 *   - window.JellyEmuConfig.ejsBase   — path prefix where three.min.js lives
 *   - window.EJS_emulator / gameManager helpers exposed by the main template
 *   - The #je-vr-btn, #je-vr-label DOM elements created by the main template
 */
(function () {
    'use strict';

    /* ── Helpers (mirrors the main template) ── */
    function emu() { return window.EJS_emulator; }
    function gm()  { var e = emu(); return e ? e.gameManager : null; }

    /* ── DOM refs ── */
    var vrBtn   = document.getElementById('je-vr-btn');
    var vrLabel = document.getElementById('je-vr-label');

    if (!vrBtn || !vrLabel) return; // guard: elements must exist

    /* ── State ── */
    var _xrSession = null;
    var _xrMode    = null; // 'immersive-ar' | 'immersive-vr'

    /* ── XR availability check ── */
    if (!navigator.xr) return;

    navigator.xr.isSessionSupported('immersive-ar').then(function (arOk) {
        // AR support is intentionally commented-out upstream; keep parity:
        // if (arOk) { _xrMode = 'immersive-ar'; … return; }
        return navigator.xr.isSessionSupported('immersive-vr').then(function (vrOk) {
            if (vrOk) {
                _xrMode             = 'immersive-vr';
                vrLabel.textContent = 'VR';
                vrBtn.title         = 'Enter VR';
                vrBtn.classList.add('je-vr-available');
            }
        });
    }).catch(function () {});

    /* ── Enter XR ── */
    function _enterXR() {
        if (!_xrMode) return;

        var gameCanvas = document.querySelector('#game canvas') || document.querySelector('canvas');
        if (!gameCanvas) return;

        if (typeof THREE === 'undefined') {
            console.error('[JellyEmu XR] Three.js is missing!');
            return;
        }

        navigator.xr.requestSession(_xrMode, {
            optionalFeatures: ['local-floor', 'bounded-floor']
        }).then(function (session) {
            _xrSession = session;

            /* ── Renderer ── */
            var renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true });
            renderer.xr.enabled = true;
            renderer.xr.setReferenceSpaceType('local-floor');
            renderer.xr.setSession(session);

            /* ── Scene ── */
            var scene  = new THREE.Scene();
            var camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.01, 20);

            scene.background = new THREE.Color(0x0a0a1a);
            scene.add(new THREE.GridHelper(20, 40, 0x00ffff, 0x111133));
            scene.add(new THREE.AmbientLight(0xffffff, 0.6));
            var dirLight = new THREE.DirectionalLight(0xffffff, 0.8);
            dirLight.position.set(5, 10, 2);
            scene.add(dirLight);

            /* ── Game-screen texture ── */
            var texture = new THREE.CanvasTexture(gameCanvas);
            texture.minFilter = THREE.NearestFilter;
            texture.magFilter = THREE.NearestFilter;

            var screenRadius = 3.5;
            var screenHeight = 2.4;
            var screenMesh   = null;

            function setAspectRatio(ratio) {
                if (screenMesh) { scene.remove(screenMesh); screenMesh.geometry.dispose(); }

                var screenWidth = screenHeight * ratio;
                var geometry    = new THREE.CylinderGeometry(
                    screenRadius, screenRadius, screenHeight, 32, 1, true,
                    -screenWidth / (2 * screenRadius), screenWidth / screenRadius
                );
                geometry.scale(-1, 1, 1);

                screenMesh = new THREE.Mesh(
                    geometry,
                    new THREE.MeshBasicMaterial({ map: texture, blending: THREE.NoBlending })
                );
                screenMesh.position.set(0, 1.5, 0);
                screenMesh.rotation.y = Math.PI;
                scene.add(screenMesh);
            }

            var currentRatioIndex = 0;
            var ratios = [4 / 3, 16 / 9, 1 / 1];
            setAspectRatio(ratios[currentRatioIndex]);

            /* ── Dock buttons ── */
            var dockGroup = new THREE.Group();
            dockGroup.position.set(0, 0.4, -2.5);
            dockGroup.rotation.x = -Math.PI / 8;
            scene.add(dockGroup);

            var interactables = [];

            function updateButtonTexture(mesh, label, isMuted, isFlashing, drawIcon) {
                var canvas = document.createElement('canvas');
                canvas.width = canvas.height = 256;
                var ctx = canvas.getContext('2d');

                ctx.fillStyle = isFlashing ? 'rgba(255,255,255,0.4)' : 'rgba(0,0,0,0.7)';
                ctx.beginPath(); ctx.roundRect(10, 10, 236, 236, 40); ctx.fill();
                ctx.lineWidth = 4;
                ctx.strokeStyle = isFlashing ? '#ffffff' : 'rgba(255,255,255,0.2)';
                ctx.stroke();

                ctx.save(); ctx.translate(128, 100);
                ctx.fillStyle = ctx.strokeStyle = '#ffffff';
                drawIcon(ctx, isMuted);
                ctx.restore();

                ctx.fillStyle = '#ffffff';
                ctx.font = 'bold 24px sans-serif';
                ctx.textAlign = 'center';
                ctx.fillText(label, 128, 200);

                if (mesh.material.map) mesh.material.map.dispose();
                mesh.material.map = new THREE.CanvasTexture(canvas);
                mesh.material.map.minFilter = THREE.LinearFilter;
                mesh.material.needsUpdate = true;
            }

            function createDockButton(action, xOffset, label, drawIcon) {
                var mesh = new THREE.Mesh(
                    new THREE.PlaneGeometry(0.4, 0.4),
                    new THREE.MeshBasicMaterial({ transparent: true })
                );
                mesh.position.set(xOffset, 0, 0);
                mesh.userData = { action: action, label: label, drawIcon: drawIcon, isMuted: false };
                updateButtonTexture(mesh, label, false, false, drawIcon);
                dockGroup.add(mesh);
                interactables.push(mesh);
                return mesh;
            }

            function drawRatioIcon(ctx) {
                ctx.lineWidth = 8; ctx.strokeRect(-40, -30, 80, 60);
            }
            function drawMuteIcon(ctx, isMuted) {
                ctx.beginPath();
                ctx.moveTo(-20, -15); ctx.lineTo(-20, 15); ctx.lineTo(0, 15);
                ctx.lineTo(25, 35);  ctx.lineTo(25, -35); ctx.closePath(); ctx.fill();
                ctx.lineWidth = 6;
                if (isMuted) {
                    ctx.strokeStyle = '#ff4444';
                    ctx.beginPath(); ctx.moveTo(35, -15); ctx.lineTo(65, 15);  ctx.stroke();
                    ctx.beginPath(); ctx.moveTo(65, -15); ctx.lineTo(35, 15);  ctx.stroke();
                } else {
                    ctx.beginPath(); ctx.arc(25, 0, 20, -Math.PI / 4, Math.PI / 4); ctx.stroke();
                    ctx.beginPath(); ctx.arc(25, 0, 35, -Math.PI / 4, Math.PI / 4); ctx.stroke();
                }
            }
            function drawExitIcon(ctx) {
                ctx.lineWidth = 10; ctx.lineCap = 'round';
                ctx.beginPath(); ctx.moveTo(-30, -30); ctx.lineTo(30,  30); ctx.stroke();
                ctx.beginPath(); ctx.moveTo( 30, -30); ctx.lineTo(-30, 30); ctx.stroke();
            }

            var btnRatio = createDockButton('ratio', -0.5, 'Ratio',   drawRatioIcon);
            var btnMute  = createDockButton('mute',   0,   'Mute',    drawMuteIcon);
            var btnExit  = createDockButton('exit',   0.5, 'Exit VR', drawExitIcon);

            /* ── Controller interaction ── */
            var raycaster  = new THREE.Raycaster();
            var tempMatrix = new THREE.Matrix4();

            function onSelect(event) {
                var controller = event.target;
                tempMatrix.identity().extractRotation(controller.matrixWorld);
                raycaster.ray.origin.setFromMatrixPosition(controller.matrixWorld);
                raycaster.ray.direction.set(0, 0, -1).applyMatrix4(tempMatrix);

                var hits = raycaster.intersectObjects(interactables);
                if (!hits.length) return;

                var btn    = hits[0].object;
                var action = btn.userData.action;

                if (action === 'ratio') {
                    currentRatioIndex = (currentRatioIndex + 1) % ratios.length;
                    setAspectRatio(ratios[currentRatioIndex]);
                    updateButtonTexture(btn, btn.userData.label, btn.userData.isMuted, true,  btn.userData.drawIcon);
                    setTimeout(function () {
                        updateButtonTexture(btn, btn.userData.label, btn.userData.isMuted, false, btn.userData.drawIcon);
                    }, 150);

                } else if (action === 'mute') {
                    btn.userData.isMuted  = !btn.userData.isMuted;
                    btn.userData.label    = btn.userData.isMuted ? 'Unmute' : 'Mute';
                    var e = emu();
                    if (e) { var v = btn.userData.isMuted ? 0 : 1.0; e.setVolume(v); e.volume = v; }
                    updateButtonTexture(btn, btn.userData.label, btn.userData.isMuted, false, btn.userData.drawIcon);

                } else if (action === 'exit') {
                    if (_xrSession) _xrSession.end();
                }
            }

            var laserGeo = new THREE.BufferGeometry().setFromPoints([
                new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, -5)
            ]);
            var laserMat = new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.5 });

            [0, 1].forEach(function (idx) {
                var ctrl = renderer.xr.getController(idx);
                ctrl.addEventListener('select', onSelect);
                ctrl.add(new THREE.Line(laserGeo, laserMat));
                scene.add(ctrl);
            });

            /* ── rAF intercept (let EJS keep running inside XR loop) ── */
            var origRaf      = window.requestAnimationFrame;
            var rafCallbacks = [];
            window.requestAnimationFrame = function (cb) {
                if (_xrSession) { rafCallbacks.push(cb); return 0; }
                return origRaf.call(window, cb);
            };

            /* ── Render loop ── */
            renderer.setAnimationLoop(function (time, frame) {
                if (!_xrSession) return;

                var cbs = rafCallbacks; rafCallbacks = [];
                for (var i = 0; i < cbs.length; i++) cbs[i](time);

                texture.needsUpdate = true;

                /* XR gamepad → emulator input passthrough */
                var g = gm();
                if (g && typeof g.simulateInput === 'function' && frame && frame.session.inputSources) {
                    for (var s = 0; s < frame.session.inputSources.length; s++) {
                        var src = frame.session.inputSources[s];
                        if (!src.gamepad) continue;
                        var pad     = src.gamepad;
                        var isLeft  = src.handedness === 'left';
                        var isRight = src.handedness === 'right';
                        var xAxis   = pad.axes.length > 2 ? pad.axes[2] : (pad.axes[0] || 0);
                        var yAxis   = pad.axes.length > 3 ? pad.axes[3] : (pad.axes[1] || 0);

                        if (isLeft) {
                            g.simulateInput(0, 6,  xAxis < -0.5 ? 1 : 0);
                            g.simulateInput(0, 7,  xAxis >  0.5 ? 1 : 0);
                            g.simulateInput(0, 4,  yAxis < -0.5 ? 1 : 0);
                            g.simulateInput(0, 5,  yAxis >  0.5 ? 1 : 0);
                            if (pad.buttons[4]) g.simulateInput(0, 9,  pad.buttons[4].pressed ? 1 : 0);
                            if (pad.buttons[5]) g.simulateInput(0, 1,  pad.buttons[5].pressed ? 1 : 0);
                            if (pad.buttons[0]) g.simulateInput(0, 12, pad.buttons[0].pressed ? 1 : 0);
                            if (pad.buttons[1]) g.simulateInput(0, 10, pad.buttons[1].pressed ? 1 : 0);
                            if (pad.buttons[3]) g.simulateInput(0, 2,  pad.buttons[3].pressed ? 1 : 0);
                        }
                        if (isRight) {
                            if (pad.buttons[4]) g.simulateInput(0, 8,  pad.buttons[4].pressed ? 1 : 0);
                            if (pad.buttons[5]) g.simulateInput(0, 0,  pad.buttons[5].pressed ? 1 : 0);
                            if (pad.buttons[0]) g.simulateInput(0, 13, pad.buttons[0].pressed ? 1 : 0);
                            if (pad.buttons[1]) g.simulateInput(0, 11, pad.buttons[1].pressed ? 1 : 0);
                            if (pad.buttons[3]) g.simulateInput(0, 3,  pad.buttons[3].pressed ? 1 : 0);
                        }
                    }
                }

                renderer.render(scene, camera);
            });

            /* Signal EJS that XR has started (button index 30 = menu/home) */
            var gCurrent = gm();
            if (gCurrent && typeof gCurrent.simulateInput === 'function') gCurrent.simulateInput(0, 30, 1);

            vrBtn.classList.add('je-vr-active');
            vrBtn.title = _xrMode === 'immersive-ar' ? 'Exit AR' : 'Exit VR';

            session.addEventListener('end', function () {
                _xrSession = null;
                vrBtn.classList.remove('je-vr-active');
                vrBtn.title = _xrMode === 'immersive-ar' ? 'Enter AR' : 'Enter VR';

                window.requestAnimationFrame = origRaf;
                renderer.dispose();

                var gEnd = gm();
                if (gEnd && typeof gEnd.simulateInput === 'function') gEnd.simulateInput(0, 30, 0);
            });

        }).catch(function (err) {
            console.warn('[JellyEmu XR] Session request failed:', err);
        });
    }

    /* ── Exit XR ── */
    function _exitXR() {
        if (_xrSession) _xrSession.end().catch(function () {});
    }

    /* ── Button toggle ── */
    vrBtn.addEventListener('click', function () {
        if (_xrSession) { _exitXR(); } else { _enterXR(); }
    });

})();