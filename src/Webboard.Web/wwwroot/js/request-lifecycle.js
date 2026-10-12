(() => {
    class LatestRequest {
        begin(target) {
            this.cancel();
            const controller = new AbortController();
            return this.current = { target, controller, signal: controller.signal };
        }
        cancel() { this.current?.controller.abort(); this.current = null; }
        isCurrent(request, target) { return this.current === request && request.target === target && !request.signal.aborted; }
    }
    window.Webboard = window.Webboard || {};
    window.Webboard.LatestRequest = LatestRequest;
})();
