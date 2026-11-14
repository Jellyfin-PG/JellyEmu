export const playerService = {
  toggleFullscreen: (): void => {
    if (!document.fullscreenElement) {
      document.documentElement.requestFullscreen().catch(() => {});
    } else {
      document.exitFullscreen().catch(() => {});
    }
  },

  exitPlayer: (): void => {
    if (window.opener) {
      window.close();
    } else {
      window.history.back();
    }
  },

  captureScreenshot: (canvasSelector: string = '#outputCanvas'): string | null => {
    const canvas = document.querySelector(canvasSelector) as HTMLCanvasElement;
    if (!canvas) return null;
    try {
      const dataUrl = canvas.toDataURL('image/png');
      const link = document.createElement('a');
      link.download = `screenshot_${Date.now()}.png`;
      link.href = dataUrl;
      link.click();
      return dataUrl;
    } catch {
      return null;
    }
  }
};
