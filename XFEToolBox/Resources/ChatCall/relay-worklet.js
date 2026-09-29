'use strict';

const TARGET_SAMPLE_RATE = 16000;
const FRAME_SAMPLE_COUNT = 320;

class RelayPcmProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.active = false;
    this.pending = new Float32Array(FRAME_SAMPLE_COUNT);
    this.pendingLength = 0;
    this.resamplePosition = 0;
    this.port.onmessage = event => {
      this.active = event.data?.active === true;
      if (!this.active) {
        this.pendingLength = 0;
        this.resamplePosition = 0;
      }
    };
  }

  process(inputs) {
    if (!this.active) return true;
    const input = inputs[0]?.[0];
    if (!input?.length) return true;

    const step = sampleRate / TARGET_SAMPLE_RATE;
    while (this.resamplePosition < input.length) {
      const index = Math.min(input.length - 1, Math.floor(this.resamplePosition));
      this.pending[this.pendingLength++] = input[index];
      this.resamplePosition += step;
      if (this.pendingLength === FRAME_SAMPLE_COUNT) this.flushFrame();
    }
    this.resamplePosition -= input.length;
    return true;
  }

  flushFrame() {
    const pcm = new Int16Array(FRAME_SAMPLE_COUNT);
    for (let index = 0; index < FRAME_SAMPLE_COUNT; index++) {
      const sample = Math.max(-1, Math.min(1, this.pending[index]));
      pcm[index] = sample < 0 ? Math.round(sample * 32768) : Math.round(sample * 32767);
    }
    this.pendingLength = 0;
    this.port.postMessage(pcm.buffer, [pcm.buffer]);
  }
}

registerProcessor('relay-pcm-processor', RelayPcmProcessor);
