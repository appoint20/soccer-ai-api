"""
AI Prediction Backtest Service.

Verifies AI predictions against actual match outcomes for past dates.
"""
from typing import Dict, List, Any, Optional
from datetime import date, datetime

from src.utils.logger import get_logger


logger = get_logger("AIPredictionBacktestService")


class AIPredictionBacktestService:
    """
    Service to verify AI predictions against actual match results.

    For past dates, loads actual outcomes and compares with AI best predictions.
    """

    def __init__(self, historical_repository):
        """
        Initialize backtest service.

        Args:
            historical_repository: Repository to fetch historical match results
        """
        self._historical_repo = historical_repository

    def verify_prediction(
        self,
        home_team: str,
        away_team: str,
        match_date: date,
        ai_best_prediction: Optional[str],
    ) -> Optional[Dict[str, Any]]:
        """
        Verify a single AI prediction against actual result.

        Args:
            home_team: Home team name
            away_team: Away team name
            match_date: Match date
            ai_best_prediction: AI's best prediction (e.g., "Over 2.5", "Home Win", "BTTS Yes")

        Returns:
            Dict with backtest result or None if no actual result found
        """
        if not ai_best_prediction:
            return None

        # Load actual result
        actual_result = self._get_actual_result(home_team, away_team, match_date)

        if not actual_result:
            logger.debug(f"No actual result found for {home_team} vs {away_team} on {match_date}")
            return None

        # Extract actual outcome
        fthg = actual_result.get("fthg")
        ftag = actual_result.get("ftag")

        if fthg is None or ftag is None:
            return None

        actual_score = f"{fthg}-{ftag}"

        # Determine actual result (H/D/A)
        if fthg > ftag:
            actual_match_result = "H"
        elif ftag > fthg:
            actual_match_result = "A"
        else:
            actual_match_result = "D"

        # Calculate actual market outcomes
        total_goals = fthg + ftag
        actual_over25 = total_goals > 2.5
        actual_btts = fthg > 0 and ftag > 0
        actual_goals_2_3 = total_goals in [2, 3]

        # Normalize AI prediction
        prediction_normalized = ai_best_prediction.strip().lower()

        # Check if prediction was correct
        was_correct, explanation = self._check_prediction_correctness(
            prediction_normalized=prediction_normalized,
            actual_over25=actual_over25,
            actual_btts=actual_btts,
            actual_goals_2_3=actual_goals_2_3,
            actual_match_result=actual_match_result,
            fthg=fthg,
            ftag=ftag,
        )

        return {
            "actual_score": actual_score,
            "actual_result": actual_match_result,
            "predicted_market": ai_best_prediction,
            "was_correct": was_correct,
            "explanation": explanation if not was_correct else None,
        }

    def _get_actual_result(
        self,
        home_team: str,
        away_team: str,
        match_date: date,
    ) -> Optional[Dict[str, Any]]:
        """
        Get actual match result from historical data.

        Args:
            home_team: Home team name
            away_team: Away team name
            match_date: Match date

        Returns:
            Match dict with actual results or None
        """
        # Get all historical matches
        all_matches = self._historical_repo.get_all()

        # Normalize team names for comparison
        home_lower = home_team.lower().strip()
        away_lower = away_team.lower().strip()

        # Search for match
        for match in all_matches:
            # Handle both dict and Match object
            if hasattr(match, 'home_team'):
                match_home = match.home_team.lower().strip()
                match_away = match.away_team.lower().strip()
                match_date_obj = match.match_date
                match_fthg = match.fthg
                match_ftag = match.ftag
            else:
                match_home = str(match.get("home_team", "")).lower().strip()
                match_away = str(match.get("away_team", "")).lower().strip()
                match_date_obj = match.get("match_date")
                match_fthg = match.get("fthg")
                match_ftag = match.get("ftag")

            # Parse date if string
            if isinstance(match_date_obj, str):
                try:
                    match_date_obj = datetime.fromisoformat(match_date_obj[:10]).date()
                except:
                    continue
            elif isinstance(match_date_obj, datetime):
                match_date_obj = match_date_obj.date()

            # Check if this is the match we're looking for
            if (match_home == home_lower and
                match_away == away_lower and
                match_date_obj == match_date):

                return {
                    "fthg": match_fthg,
                    "ftag": match_ftag,
                }

        return None

    def _check_prediction_correctness(
        self,
        prediction_normalized: str,
        actual_over25: bool,
        actual_btts: bool,
        actual_goals_2_3: bool,
        actual_match_result: str,
        fthg: int,
        ftag: int,
    ) -> tuple[bool, Optional[str]]:
        """
        Check if AI prediction was correct.

        Returns:
            (was_correct, explanation)
        """
        # Over 2.5 Goals
        if "over 2.5" in prediction_normalized or "over2.5" in prediction_normalized:
            if actual_over25:
                return True, None
            else:
                return False, f"Predicted Over 2.5 but match ended {fthg}-{ftag} (Under 2.5)"

        # Under 2.5 Goals
        if "under 2.5" in prediction_normalized or "under2.5" in prediction_normalized:
            if not actual_over25:
                return True, None
            else:
                return False, f"Predicted Under 2.5 but match ended {fthg}-{ftag} (Over 2.5)"

        # BTTS Yes
        if "btts yes" in prediction_normalized or "btts" in prediction_normalized and "no" not in prediction_normalized:
            if actual_btts:
                return True, None
            else:
                return False, f"Predicted BTTS Yes but match ended {fthg}-{ftag}"

        # BTTS No
        if "btts no" in prediction_normalized:
            if not actual_btts:
                return True, None
            else:
                return False, f"Predicted BTTS No but match ended {fthg}-{ftag}"

        # 2-3 Goals
        if "2-3 goals" in prediction_normalized or "2-3" in prediction_normalized:
            if actual_goals_2_3:
                return True, None
            else:
                return False, f"Predicted 2-3 Goals but match ended {fthg}-{ftag} ({fthg + ftag} goals)"

        # Home Win
        if "home win" in prediction_normalized or "home" in prediction_normalized:
            if actual_match_result == "H":
                return True, None
            else:
                result_text = "Draw" if actual_match_result == "D" else "Away Win"
                return False, f"Predicted Home Win but result was {result_text} ({fthg}-{ftag})"

        # Away Win
        if "away win" in prediction_normalized or "away" in prediction_normalized:
            if actual_match_result == "A":
                return True, None
            else:
                result_text = "Draw" if actual_match_result == "D" else "Home Win"
                return False, f"Predicted Away Win but result was {result_text} ({fthg}-{ftag})"

        # Draw
        if "draw" in prediction_normalized:
            if actual_match_result == "D":
                return True, None
            else:
                result_text = "Home Win" if actual_match_result == "H" else "Away Win"
                return False, f"Predicted Draw but result was {result_text} ({fthg}-{ftag})"

        # Unknown prediction format
        return False, f"Could not evaluate prediction '{prediction_normalized}'"

    def calculate_daily_stats(
        self,
        backtest_results: List[Dict[str, Any]],
    ) -> Dict[str, Any]:
        """
        Calculate aggregated statistics for a day's predictions.

        Args:
            backtest_results: List of backtest result dicts

        Returns:
            Dict with accuracy statistics
        """
        if not backtest_results:
            return {
                "total_predictions": 0,
                "correct_predictions": 0,
                "incorrect_predictions": 0,
                "accuracy_percentage": 0.0,
                "by_market": {},
            }

        total = len(backtest_results)
        correct = sum(1 for r in backtest_results if r.get("was_correct", False))
        incorrect = total - correct
        accuracy = (correct / total * 100) if total > 0 else 0.0

        # Breakdown by market
        by_market = {}
        for result in backtest_results:
            market = result.get("predicted_market", "Unknown")

            if market not in by_market:
                by_market[market] = {"correct": 0, "total": 0}

            by_market[market]["total"] += 1
            if result.get("was_correct", False):
                by_market[market]["correct"] += 1

        # Add accuracy percentage for each market
        for market, stats in by_market.items():
            stats["accuracy"] = (stats["correct"] / stats["total"] * 100) if stats["total"] > 0 else 0.0

        return {
            "total_predictions": total,
            "correct_predictions": correct,
            "incorrect_predictions": incorrect,
            "accuracy_percentage": round(accuracy, 2),
            "by_market": by_market,
        }
