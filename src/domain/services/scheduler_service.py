import asyncio
from datetime import datetime
from src.utils.logger import get_logger
from src.api.dependencies import ServiceContainer
from src.api.schemas import ModelTier

class SchedulerService:
    """
    Simple background scheduler for periodic tasks.
    Replaces apscheduler to avoid extra dependencies.
    """
    
    def __init__(self):
        self.logger = get_logger("SchedulerService")
        self._running = False
        self._task = None
        self._check_interval = 3600  # Check every hour

    async def start(self):
        """Start the scheduler loop."""
        if self._running:
            return
            
        self._running = True
        self._task = asyncio.create_task(self._job_loop())
        self.logger.info("Scheduler started. Checking for tasks every hour.")

    async def stop(self):
        """Stop the scheduler loop."""
        self._running = False
        if self._task:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        self.logger.info("Scheduler stopped")

    async def _job_loop(self):
        """Main loop checking for scheduled tasks."""
        while self._running:
            try:
                await self._check_weekly_retrain()
            except Exception as e:
                self.logger.error(f"Scheduler error: {e}")
            
            # Wait for next check
            await asyncio.sleep(self._check_interval)

    async def _check_weekly_retrain(self):
        """Check if we should run weekly model retraining."""
        now = datetime.now()
        
        # 1 = Tuesday (Monday is 0, Sunday is 6)
        if now.weekday() == 1:
            service = ServiceContainer.retraining_service
            
            # Safety check if service is initialized
            if not service:
                self.logger.warning("Retraining service not available, skipping schedule check")
                return

            status = service.get_status()
            last_run_iso = status.get("timestamp")
            
            should_run = True
            
            # Check if already ran today (in this process lifetime)
            if last_run_iso:
                try:
                    last_run = datetime.fromisoformat(last_run_iso)
                    # If ran today and status was success/running, don't run again
                    if last_run.date() == now.date() and status.get("status") in ["success", "running"]:
                        should_run = False
                except ValueError:
                    pass  # Invalid date format, default to run
            
            if should_run:
                self.logger.info("It is Tuesday. Triggering scheduled retraining for Tier 1.")
                
                # Run sync method in executor to avoid blocking event loop
                loop = asyncio.get_event_loop()
                await loop.run_in_executor(None, service.start_retraining, ModelTier.TIER1)
