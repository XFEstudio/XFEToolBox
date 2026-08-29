'use strict';

const state = {
  config: null,
  audioContext: null,
  rawStream: null,
  processedStream: null,
  sourceNode: null,
  micGainNode: null,
  limiterNode: null,
  peers: new Map(),
  participantNames: new Map(),
  micMuted: false,
  noiseSuppression: true,
  currentDeviceId: '',
  initialized: false,
  stopping: false,
  disposed: false,
  metricsTimer: null
};

let hostMessageChain = Promise.resolve();

const ui = {
  status: document.getElementById('statusText'),
  meshBadge: document.getElementById('meshBadge'),
  noiseBadge: document.getElementById('noiseBadge'),
  turnBadge: document.getElementById('turnBadge'),
  participantCount: document.getElementById('participantCount'),
  roundTrip: document.getElementById('roundTrip'),
  jitter: document.getElementById('jitter'),
  packetLoss: document.getElementById('packetLoss'),
  grid: document.getElementById('participantGrid'),
  template: document.getElementById('participantTemplate'),
  microphone: document.getElementById('microphoneSelect'),
  noiseButton: document.getElementById('noiseButton'),
  muteButton: document.getElementById('muteButton'),
  muteLabel: document.getElementById('muteLabel'),
  micGain: document.getElementById('micGain'),
  micGainValue: document.getElementById('micGainValue'),
  leave: document.getElementById('leaveButton')
};

function post(message) {
  if (state.disposed) return;
  window.chrome?.webview?.postMessage(message);
}

function reportStatus(message) {
  ui.status.textContent = message;
  post({ type: 'status', message });
}

function reportError(error) {
  const message = error instanceof Error ? error.message : String(error);
  ui.status.textContent = message;
  post({ type: 'error', message });
}

window.chrome.webview.addEventListener('message', event => {
  hostMessageChain = hostMessageChain
    .then(() => handleHostMessage(event.data))
    .catch(reportError);
});

async function handleHostMessage(message) {
  if (!message || typeof message.type !== 'string') return;
  switch (message.type) {
    case 'host.init':
      await initialize(message);
      break;
    case 'call.participants':
      await reconcileParticipants(message.participants || []);
      break;
    case 'call.participant-left':
      removePeer(message.userId);
      break;
    case 'webrtc.signal':
      await handleRemoteSignal(message.signalType, message.fromUserId, message.signal || {});
      break;
    case 'realtime.state':
      if (message.state !== 'Connected') reportStatus('信令连接暂时中断，正在恢复…');
      break;
    case 'host.dispose':
      await disposeCall();
      break;
  }
}

async function initialize(config) {
  if (state.initialized || state.disposed) return;
  state.initialized = true;
  state.config = config;
  state.noiseSuppression = config.startWithNoiseSuppression !== false;
  ui.noiseButton.classList.toggle('active', state.noiseSuppression);
  ui.noiseButton.setAttribute('aria-pressed', String(state.noiseSuppression));
  ui.meshBadge.textContent = `小群 Mesh · 最多 ${config.maximumParticipants} 人`;

  const urls = (config.iceServers || []).flatMap(item => Array.isArray(item.urls) ? item.urls : [item.urls]);
  const hasTurn = urls.some(url => typeof url === 'string' && /^(turn|turns):/i.test(url));
  ui.turnBadge.textContent = hasTurn ? 'TURN 中继已配置' : '未配置 TURN · 复杂 NAT 可能失败';
  ui.turnBadge.classList.toggle('warning', !hasTurn);

  const supported = navigator.mediaDevices.getSupportedConstraints();
  ui.noiseBadge.textContent = supported.voiceIsolation
    ? 'Voice Isolation 可用'
    : 'WebRTC AEC / NS / AGC';
  ui.noiseBadge.classList.remove('muted');

  renderSelfCard();
  await acquireMicrophone();
  await refreshMicrophoneList();
  reportStatus('麦克风已就绪，等待其他成员加入');
  state.metricsTimer = setInterval(() => void updateNetworkMetrics(), 2000);
}

function buildAudioConstraints() {
  const supported = navigator.mediaDevices.getSupportedConstraints();
  const audio = {
    echoCancellation: true,
    noiseSuppression: state.noiseSuppression,
    autoGainControl: true,
    channelCount: 1,
    sampleRate: 48000,
    sampleSize: 16
  };
  if (supported.voiceIsolation) audio.voiceIsolation = state.noiseSuppression;
  if (state.currentDeviceId) audio.deviceId = { exact: state.currentDeviceId };
  return { audio, video: false };
}

async function acquireMicrophone() {
  if (state.disposed) return;
  reportStatus('正在申请麦克风权限…');
  const nextRawStream = await navigator.mediaDevices.getUserMedia(buildAudioConstraints());
  if (!state.audioContext) state.audioContext = new AudioContext({ latencyHint: 'interactive', sampleRate: 48000 });
  if (state.audioContext.state === 'suspended') await state.audioContext.resume();

  const oldRawStream = state.rawStream;
  const oldProcessedStream = state.processedStream;
  if (state.sourceNode) state.sourceNode.disconnect();

  const source = state.audioContext.createMediaStreamSource(nextRawStream);
  const highPass = state.audioContext.createBiquadFilter();
  highPass.type = 'highpass';
  highPass.frequency.value = 80;
  highPass.Q.value = .7;
  const lowPass = state.audioContext.createBiquadFilter();
  lowPass.type = 'lowpass';
  lowPass.frequency.value = 12000;
  lowPass.Q.value = .5;
  const gain = state.audioContext.createGain();
  const limiter = state.audioContext.createDynamicsCompressor();
  limiter.threshold.value = -3;
  limiter.knee.value = 0;
  limiter.ratio.value = 20;
  limiter.attack.value = .003;
  limiter.release.value = .08;
  const destination = state.audioContext.createMediaStreamDestination();
  source.connect(highPass).connect(lowPass).connect(gain).connect(limiter).connect(destination);

  state.rawStream = nextRawStream;
  state.processedStream = destination.stream;
  state.sourceNode = source;
  state.micGainNode = gain;
  state.limiterNode = limiter;
  applyMicrophoneGain();

  const nextTrack = destination.stream.getAudioTracks()[0];
  for (const peer of state.peers.values()) {
    const sender = peer.pc.getSenders().find(item => item.track?.kind === 'audio');
    if (sender) await sender.replaceTrack(nextTrack);
  }
  oldRawStream?.getTracks().forEach(track => track.stop());
  oldProcessedStream?.getTracks().forEach(track => track.stop());
}

async function refreshMicrophoneList() {
  const devices = (await navigator.mediaDevices.enumerateDevices()).filter(item => item.kind === 'audioinput');
  const activeDevice = state.rawStream?.getAudioTracks()[0]?.getSettings().deviceId || state.currentDeviceId;
  ui.microphone.replaceChildren();
  devices.forEach((device, index) => {
    const option = document.createElement('option');
    option.value = device.deviceId;
    option.textContent = device.label || `麦克风 ${index + 1}`;
    option.selected = device.deviceId === activeDevice;
    ui.microphone.append(option);
  });
  ui.microphone.disabled = devices.length === 0;
  state.currentDeviceId = activeDevice || devices[0]?.deviceId || '';
}

function applyMicrophoneGain() {
  const gain = state.micMuted ? 0 : Number(ui.micGain.value) / 100;
  if (state.micGainNode) state.micGainNode.gain.setTargetAtTime(gain, state.audioContext.currentTime, .015);
  const track = state.processedStream?.getAudioTracks()[0];
  if (track) track.enabled = !state.micMuted;
  ui.micGainValue.value = `${ui.micGain.value}%`;
}

function renderSelfCard() {
  const card = createParticipantCard(state.config.selfUserId, state.config.selfDisplayName, true);
  card.querySelector('.identity span').textContent = '本机 · WebRTC 音频处理';
  card.querySelector('.connection-dot').classList.add('connected');
  ui.grid.prepend(card);
  updateParticipantCount();
}

function createParticipantCard(userId, displayName, self = false) {
  const existing = document.getElementById(`peer-${safeDomId(userId)}`);
  if (existing) return existing;
  const card = ui.template.content.firstElementChild.cloneNode(true);
  card.id = `peer-${safeDomId(userId)}`;
  card.dataset.userId = userId;
  card.classList.toggle('self', self);
  card.querySelector('.avatar').textContent = initials(displayName);
  card.querySelector('.identity strong').textContent = self ? `${displayName}（我）` : displayName;
  const controls = card.querySelector('.volume-row');
  if (self) controls.remove();
  ui.grid.append(card);
  return card;
}

async function reconcileParticipants(participants) {
  if (state.disposed) return;
  if (!state.config || !state.processedStream)
    throw new Error('通话音频尚未完成初始化。');
  const desired = new Set();
  for (const participant of participants) {
    if (!participant?.userId || participant.userId === state.config.selfUserId) continue;
    desired.add(participant.userId);
    state.participantNames.set(participant.userId, participant.displayName || participant.userId);
    const shouldOffer = state.config.selfUserId.localeCompare(participant.userId, 'en') < 0;
    await ensurePeer(participant.userId, participant.displayName || participant.userId, shouldOffer);
  }
  for (const userId of [...state.peers.keys()]) {
    if (!desired.has(userId)) removePeer(userId);
  }
  updateParticipantCount();
}

async function ensurePeer(userId, displayName, initiate) {
  if (state.peers.has(userId)) return state.peers.get(userId);
  if (state.peers.size >= state.config.maximumParticipants - 1) throw new Error(`小群 Mesh 最多支持 ${state.config.maximumParticipants} 人。`);

  const card = createParticipantCard(userId, displayName, false);
  const pc = new RTCPeerConnection({
    iceServers: state.config.iceServers || [],
    bundlePolicy: 'max-bundle',
    rtcpMuxPolicy: 'require',
    iceCandidatePoolSize: 2
  });
  const peer = { userId, displayName, pc, card, gainNode: null, sourceNode: null, pendingIce: [], muted: false, volume: 1 };
  state.peers.set(userId, peer);

  const localTrack = state.processedStream.getAudioTracks()[0];
  const sender = pc.addTrack(localTrack, state.processedStream);
  preferOpus(pc, sender);
  pc.onicecandidate = event => {
    if (event.candidate) sendSignal('webrtc.ice', userId, { candidate: event.candidate.toJSON() });
  };
  pc.ontrack = event => attachRemoteAudio(peer, event.streams[0] || new MediaStream([event.track]));
  pc.onconnectionstatechange = () => updatePeerConnectionState(peer);
  pc.oniceconnectionstatechange = () => updatePeerConnectionState(peer);
  bindPeerControls(peer);
  updateParticipantCount();

  if (initiate) {
    await pc.setLocalDescription(await pc.createOffer({ offerToReceiveAudio: true }));
    sendSignal('webrtc.offer', userId, { sdp: pc.localDescription });
  }
  return peer;
}

function preferOpus(pc, sender) {
  const transceiver = pc.getTransceivers().find(item => item.sender === sender);
  const capabilities = RTCRtpSender.getCapabilities?.('audio');
  if (!transceiver?.setCodecPreferences || !capabilities?.codecs) return;
  const codecs = [...capabilities.codecs].sort((left, right) => {
    const leftOpus = left.mimeType.toLowerCase() === 'audio/opus' ? 1 : 0;
    const rightOpus = right.mimeType.toLowerCase() === 'audio/opus' ? 1 : 0;
    return rightOpus - leftOpus;
  });
  transceiver.setCodecPreferences(codecs);
}

function attachRemoteAudio(peer, stream) {
  if (peer.sourceNode) peer.sourceNode.disconnect();
  const source = state.audioContext.createMediaStreamSource(stream);
  const gain = state.audioContext.createGain();
  gain.gain.value = peer.muted ? 0 : peer.volume;
  source.connect(gain).connect(state.audioContext.destination);
  peer.sourceNode = source;
  peer.gainNode = gain;
  peer.card.querySelector('.identity span').textContent = '语音轨道已接收';
}

function bindPeerControls(peer) {
  const mute = peer.card.querySelector('.peer-mute');
  const volume = peer.card.querySelector('.peer-volume');
  const output = peer.card.querySelector('output');
  mute.addEventListener('click', () => {
    peer.muted = !peer.muted;
    mute.setAttribute('aria-pressed', String(peer.muted));
    mute.textContent = peer.muted ? '已静音' : '静音';
    applyPeerGain(peer);
  });
  volume.addEventListener('input', () => {
    peer.volume = Number(volume.value) / 100;
    output.value = `${volume.value}%`;
    applyPeerGain(peer);
  });
}

function applyPeerGain(peer) {
  if (peer.gainNode) peer.gainNode.gain.setTargetAtTime(peer.muted ? 0 : peer.volume, state.audioContext.currentTime, .015);
}

async function handleRemoteSignal(signalType, fromUserId, signal) {
  if (state.disposed) return;
  const displayName = state.participantNames.get(fromUserId) || fromUserId;
  const peer = await ensurePeer(fromUserId, displayName, false);
  if (signalType === 'webrtc.offer') {
    await peer.pc.setRemoteDescription(signal.sdp);
    await flushPendingIce(peer);
    await peer.pc.setLocalDescription(await peer.pc.createAnswer());
    sendSignal('webrtc.answer', fromUserId, { sdp: peer.pc.localDescription });
  } else if (signalType === 'webrtc.answer') {
    await peer.pc.setRemoteDescription(signal.sdp);
    await flushPendingIce(peer);
  } else if (signalType === 'webrtc.ice' && signal.candidate) {
    if (peer.pc.remoteDescription) await peer.pc.addIceCandidate(signal.candidate);
    else peer.pendingIce.push(signal.candidate);
  }
}

async function flushPendingIce(peer) {
  while (peer.pendingIce.length) await peer.pc.addIceCandidate(peer.pendingIce.shift());
}

function sendSignal(signalType, targetUserId, detail) {
  const payload = { callId: state.config.callId, targetUserId, ...detail };
  post({ type: 'signal', callId: state.config.callId, signalType, targetUserId, payload });
}

function updatePeerConnectionState(peer) {
  const connectionState = peer.pc.connectionState;
  const dot = peer.card.querySelector('.connection-dot');
  dot.classList.toggle('connected', connectionState === 'connected');
  dot.classList.toggle('failed', connectionState === 'failed' || connectionState === 'closed');
  peer.card.querySelector('.identity span').textContent = ({
    new: '等待协商', connecting: '正在建立低延迟链路', connected: '已连接 · Opus',
    disconnected: '连接暂时中断', failed: '连接失败', closed: '已离开'
  })[connectionState] || connectionState;
}

function removePeer(userId) {
  const peer = state.peers.get(userId);
  if (!peer) return;
  peer.sourceNode?.disconnect();
  peer.pc.ontrack = null;
  peer.pc.onicecandidate = null;
  peer.pc.close();
  peer.card.remove();
  state.peers.delete(userId);
  state.participantNames.delete(userId);
  updateParticipantCount();
}

async function updateNetworkMetrics() {
  if (state.disposed) return;
  let maxRtt = null;
  let maxJitter = null;
  let lost = 0;
  let received = 0;
  for (const peer of state.peers.values()) {
    const stats = await peer.pc.getStats();
    stats.forEach(report => {
      if (report.type === 'candidate-pair' && report.state === 'succeeded' && Number.isFinite(report.currentRoundTripTime))
        maxRtt = Math.max(maxRtt || 0, report.currentRoundTripTime);
      if (report.type === 'inbound-rtp' && report.kind === 'audio') {
        if (Number.isFinite(report.jitter)) maxJitter = Math.max(maxJitter || 0, report.jitter);
        lost += Math.max(0, report.packetsLost || 0);
        received += Math.max(0, report.packetsReceived || 0);
      }
    });
  }
  ui.roundTrip.textContent = maxRtt == null ? '—' : `${Math.round(maxRtt * 1000)} ms`;
  ui.jitter.textContent = maxJitter == null ? '—' : `${Math.round(maxJitter * 1000)} ms`;
  ui.packetLoss.textContent = received + lost === 0 ? '—' : `${(lost / (received + lost) * 100).toFixed(1)}%`;
}

function updateParticipantCount() {
  ui.participantCount.textContent = String(1 + state.peers.size);
}

function initials(name) {
  const trimmed = String(name || '?').trim();
  return [...trimmed].slice(0, 2).join('').toUpperCase();
}

function safeDomId(value) {
  return [...String(value)].map(character => /[a-zA-Z0-9_-]/.test(character) ? character : `_${character.codePointAt(0).toString(16)}`).join('');
}

ui.microphone.addEventListener('change', () => {
  if (state.disposed) return;
  state.currentDeviceId = ui.microphone.value;
  void acquireMicrophone().then(refreshMicrophoneList).then(() => reportStatus('已切换麦克风')).catch(reportError);
});

ui.noiseButton.addEventListener('click', () => {
  if (state.disposed) return;
  state.noiseSuppression = !state.noiseSuppression;
  ui.noiseButton.classList.toggle('active', state.noiseSuppression);
  ui.noiseButton.setAttribute('aria-pressed', String(state.noiseSuppression));
  void acquireMicrophone().then(() => reportStatus(state.noiseSuppression ? 'WebRTC 降噪已开启' : 'WebRTC 降噪已关闭')).catch(reportError);
});

ui.muteButton.addEventListener('click', () => {
  if (state.disposed) return;
  state.micMuted = !state.micMuted;
  ui.muteButton.classList.toggle('active', !state.micMuted);
  ui.muteButton.setAttribute('aria-pressed', String(state.micMuted));
  ui.muteLabel.textContent = state.micMuted ? '麦克风静音' : '麦克风开启';
  applyMicrophoneGain();
});

ui.micGain.addEventListener('input', applyMicrophoneGain);
ui.leave.addEventListener('click', () => {
  if (state.stopping || state.disposed) return;
  state.stopping = true;
  post({ type: 'leave', callId: state.config?.callId });
});

document.addEventListener('pointerdown', () => {
  if (state.audioContext?.state === 'suspended') void state.audioContext.resume();
}, { once: true });

async function disposeCall() {
  if (state.disposed) return;
  state.disposed = true;
  state.stopping = true;
  if (state.metricsTimer != null) {
    clearInterval(state.metricsTimer);
    state.metricsTimer = null;
  }
  for (const userId of [...state.peers.keys()]) removePeer(userId);
  state.sourceNode?.disconnect();
  state.micGainNode?.disconnect();
  state.limiterNode?.disconnect();
  state.rawStream?.getTracks().forEach(track => track.stop());
  state.processedStream?.getTracks().forEach(track => track.stop());
  state.rawStream = null;
  state.processedStream = null;
  state.sourceNode = null;
  state.micGainNode = null;
  state.limiterNode = null;
  const context = state.audioContext;
  state.audioContext = null;
  if (context && context.state !== 'closed') {
    try { await context.close(); } catch { }
  }
}

window.addEventListener('beforeunload', () => {
  void disposeCall();
});

post({ type: 'ready' });
