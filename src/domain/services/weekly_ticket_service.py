"""
Weekly ticket generation service.

Rules:
- 5 tickets per week
- 2 mixed tickets (can include wins/draws/over25/btts) - max 1 win/draw each
- 3 goals-only tickets (only over25/btts)
- Max 2 games from same league per ticket
- Min odds: 1.60 for over25/btts, 2.0 for wins
"""
from datetime import datetime, timedelta
from typing import Dict, List, Any, Optional
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.prediction_service import PredictionService
from src.data.cache.cache_manager import CacheManager
from src.utils.logger import get_logger


class WeeklyTicketService(BaseService):
    """Service for generating weekly betting tickets."""
    
    # Odds constraints
    MIN_ODDS_GOALS = 1.60  # Over25/BTTS
    MIN_ODDS_RESULT = 2.0  # Win/Draw
    
    # Ticket constraints
    MAX_GAMES_PER_LEAGUE = 2
    SELECTIONS_PER_TICKET = 3
    
    def __init__(self, cache_manager: Optional[CacheManager] = None):
        super().__init__(cache_manager)
        self.logger = get_logger("WeeklyTicketService")
        self.match_stats = MatchStatsService(cache_manager)
        self.prediction_service = PredictionService()
        self.prediction_service.load_models()
    
    def generate_weekly_tickets(
        self,
        matches: List[Dict],
        week_start: str,
    ) -> Dict[str, Any]:
        """
        Generate 5 weekly tickets.
        
        Args:
            matches: All historical matches for context
            week_start: Start date of the week (YYYY-MM-DD)
            
        Returns:
            Dict with mixed_tickets and goals_only_tickets
        """
        start = datetime.strptime(week_start, "%Y-%m-%d")
        end = start + timedelta(days=7)
        end_str = end.strftime("%Y-%m-%d")
        
        # Get matches for this week
        all_week_matches = [
            m for m in matches
            if week_start <= str(m.get("match_date", ""))[:10] < end_str
        ]
        
        if not all_week_matches:
            self.logger.info(f"No matches found for week {week_start}")
            return {
                "week_start": week_start,
                "week_end": end_str,
                "mixed_tickets": [],
                "goals_only_tickets": [],
                "total_tickets": 0,
                "generated_at": datetime.now().isoformat(),
            }

        # Restriction: Only take matches spread on two days (busiest 2-day window)
        # 1. Group by date
        by_date = defaultdict(list)
        for m in all_week_matches:
            d = str(m.get("match_date", ""))[:10]
            if d:
                by_date[d].append(m)
        
        # 2. Find best 2 consecutive days (max total matches)
        sorted_dates = sorted(by_date.keys())
        best_window = []
        max_count = 0
        
        # Check single days generally, but finding best 2-day span
        if len(sorted_dates) == 1:
            best_window = [sorted_dates[0]]
        else:
            for i in range(len(sorted_dates)-1):
                d1 = sorted_dates[i]
                d2 = sorted_dates[i+1]
                
                # Check if consecutive (or close enough to be considered a "weekend" block)
                # Ideally check datetime delta, but for now just picking max sum
                count = len(by_date[d1]) + len(by_date[d2])
                if count > max_count:
                    max_count = count
                    best_window = [d1, d2]
        
        # 3. Filter matches to this window
        week_matches = []
        for d in best_window:
            week_matches.extend(by_date[d])

        self.logger.info(f"Week {week_start}: Selected 2-day window {best_window} with {len(week_matches)} matches")
        
        self.logger.info(f"Found {len(week_matches)} matches for week {week_start}")
        
        # Analyze all matches
        analyzed = []
        for match in week_matches:
            analysis = self._analyze_match(match, matches)
            if analysis:
                analyzed.append(analysis)
        
        self.logger.info(f"Analyzed {len(analyzed)} matches")
        
        # Track used matches across ALL tickets to prevent duplicates
        used_matches = set()
        
        # Generate tickets
        mixed_tickets = self._generate_mixed_tickets(analyzed, 2, used_matches)
        goals_tickets = self._generate_goals_only_tickets(analyzed, 3, used_matches)
        
        return {
            "week_start": week_start,
            "week_end": end_str,
            "mixed_tickets": mixed_tickets,
            "goals_only_tickets": goals_tickets,
            "total_tickets": len(mixed_tickets) + len(goals_tickets),
            "generated_at": datetime.now().isoformat(),
        }
    
    def _analyze_match(
        self,
        match: Dict,
        all_matches: List[Dict],
    ) -> Optional[Dict[str, Any]]:
        """Analyze a single match for ticket selection."""
        home = match.get("home_team", "")
        away = match.get("away_team", "")
        league = match.get("league", "")
        date = str(match.get("match_date", ""))[:10]
        
        if not home or not away:
            return None
        
        # Get predictions
        pred = self.prediction_service.predict_match(match, all_matches)
        
        # Get qualification
        match_date_str = date
        match_date_obj = datetime.strptime(match_date_str, "%Y-%m-%d").date() if match_date_str else None
        
        # DEBUG LOGGING
        self.logger.info(f"Analyizing match {home} vs {away}. Date: {match_date_str}. Type: {type(match_date_obj)}")
        
        stats = self.match_stats.calculate_match_stats(
            home, away, all_matches, 
            league=league,
            as_of_date=match_date_obj
        )
        qual = stats.get("qualification", {})
        
        # Extract ACTUAL odds from match data (lowercase field names)
        home_odds = float(match.get("b365h") or match.get("B365H") or 0)
        draw_odds = float(match.get("b365d") or match.get("B365D") or 0)
        away_odds = float(match.get("b365a") or match.get("B365A") or 0)
        over25_odds = float(match.get("b365_over25") or match.get("B365>2.5") or 0)
        # BTTS odds not typically in data, estimate from over25
        btts_odds = over25_odds * 0.97 if over25_odds else 0  # Slightly lower than over25
        
        # Get confidences
        over25_pred = pred.get("over25", {})
        btts_pred = pred.get("btts", {})
        result_pred = pred.get("result", {})
        result_probs = result_pred.get("probabilities", {})
        
        return {
            "match_id": f"{league}_{date}_{home}_{away}".replace(" ", ""),
            "home_team": home,
            "away_team": away,
            "league": league,
            "date": date,
            "odds": {
                "home_win": home_odds,
                "draw": draw_odds,
                "away_win": away_odds,
                "over25": over25_odds,
                "btts": btts_odds,
            },
            "predictions": {
                "over25": over25_pred.get("prediction", "NO"),
                "over25_conf": float(over25_pred.get("probability", 0.5)),
                "btts": btts_pred.get("prediction", "NO"),
                "btts_conf": float(btts_pred.get("probability", 0.5)),
                "result": result_pred.get("prediction", "D"),
                "home_win_conf": result_probs.get("home_win", 0.33),
                "draw_conf": result_probs.get("draw", 0.34),
                "away_win_conf": result_probs.get("away_win", 0.33),
            },
            "qualified": {
                "over25": qual.get("over25_qualified", False),
                "btts": qual.get("btts_qualified", False),
            },
            "low_scoring_stats": stats.get("low_scoring", {}),
        }
    
    def _generate_mixed_tickets(
        self,
        analyzed: List[Dict],
        count: int,
        used_matches: set,
    ) -> List[Dict]:
        """
        Generate mixed tickets (1 win + goals markets).
        
        Rules:
        - Max 1 win per ticket (NO DRAWS)
        - Max 2 games from same league
        - Min odds: 2.0 for result, 1.60 for goals
        - No match appears in multiple tickets
        """
        tickets = []
        
        for ticket_id in range(1, count + 1):
            selections = []
            league_count = defaultdict(int)
            has_result = False
            
            # First, try to add one result selection
            result_candidates = [
                m for m in analyzed
                if m["match_id"] not in used_matches
                and self._get_best_result_selection(m) is not None
            ]
            
            # Sort by confidence
            result_candidates.sort(
                key=lambda m: max(
                    m["predictions"]["home_win_conf"],
                    m["predictions"]["draw_conf"],
                    m["predictions"]["away_win_conf"]
                ),
                reverse=True
            )
            
            for match in result_candidates[:5]:
                if league_count[match["league"]] >= self.MAX_GAMES_PER_LEAGUE:
                    continue
                
                sel = self._get_best_result_selection(match)
                if sel and sel["odds"] >= self.MIN_ODDS_RESULT:
                    selections.append(sel)
                    used_matches.add(match["match_id"])
                    league_count[match["league"]] += 1
                    has_result = True
                    break
            
            # Then add goals selections
            goals_candidates = [
                m for m in analyzed
                if m["match_id"] not in used_matches
                and (m["qualified"]["over25"] or m["qualified"]["btts"])
            ]
            
            # Sort by qualification + confidence
            goals_candidates.sort(
                key=lambda m: max(
                    m["predictions"]["over25_conf"] if m["qualified"]["over25"] else 0,
                    m["predictions"]["btts_conf"] if m["qualified"]["btts"] else 0
                ),
                reverse=True
            )
            
            for match in goals_candidates:
                if len(selections) >= self.SELECTIONS_PER_TICKET:
                    break
                
                if league_count[match["league"]] >= self.MAX_GAMES_PER_LEAGUE:
                    continue
                
                sel = self._get_best_goals_selection(match)
                if sel and sel["odds"] >= self.MIN_ODDS_GOALS:
                    selections.append(sel)
                    used_matches.add(match["match_id"])
                    league_count[match["league"]] += 1
            
            if selections:
                total_odds = 1.0
                for s in selections:
                    total_odds *= s["odds"]
                
                tickets.append({
                    "ticket_id": ticket_id,
                    "ticket_type": "mixed",
                    "selections": selections,
                    "total_odds": round(total_odds, 2),
                    "expected_return": round(10 * total_odds, 2),
                })
        
        return tickets
    
    def _generate_goals_only_tickets(
        self,
        analyzed: List[Dict],
        count: int,
        used_matches: set,
    ) -> List[Dict]:
        """
        Generate goals-only tickets (over25/btts only).
        
        Rules:
        - Only over25 and btts markets
        - Max 2 games from same league
        - Min odds: 1.60
        - Prefer qualified matches
        - No match appears in multiple tickets
        """
        tickets = []
        
        for ticket_id in range(1, count + 1):
            selections = []
            league_count = defaultdict(int)
            
            # Get qualified goals candidates
            goals_candidates = [
                m for m in analyzed
                if m["match_id"] not in used_matches
                and (m["qualified"]["over25"] or m["qualified"]["btts"])
            ]
            
            # Sort by confidence
            goals_candidates.sort(
                key=lambda m: max(
                    m["predictions"]["over25_conf"] if m["qualified"]["over25"] else 0,
                    m["predictions"]["btts_conf"] if m["qualified"]["btts"] else 0
                ),
                reverse=True
            )
            
            for match in goals_candidates:
                if len(selections) >= self.SELECTIONS_PER_TICKET:
                    break
                
                if league_count[match["league"]] >= self.MAX_GAMES_PER_LEAGUE:
                    continue
                
                sel = self._get_best_goals_selection(match)
                if sel and sel["odds"] >= self.MIN_ODDS_GOALS:
                    selections.append(sel)
                    used_matches.add(match["match_id"])
                    league_count[match["league"]] += 1
            
            # If not enough qualified, add non-qualified with high confidence
            if len(selections) < self.SELECTIONS_PER_TICKET:
                non_qual = [
                    m for m in analyzed
                    if m["match_id"] not in used_matches
                    and not m["qualified"]["over25"]
                    and not m["qualified"]["btts"]
                    and (m["predictions"]["over25_conf"] > 0.6 or m["predictions"]["btts_conf"] > 0.6)
                ]
                
                non_qual.sort(
                    key=lambda m: max(m["predictions"]["over25_conf"], m["predictions"]["btts_conf"]),
                    reverse=True
                )
                
                for match in non_qual:
                    if len(selections) >= self.SELECTIONS_PER_TICKET:
                        break
                    
                    if league_count[match["league"]] >= self.MAX_GAMES_PER_LEAGUE:
                        continue
                    
                    sel = self._get_best_goals_selection(match)
                    if sel and sel["odds"] >= self.MIN_ODDS_GOALS:
                        selections.append(sel)
                        used_matches.add(match["match_id"])
                        league_count[match["league"]] += 1
            
            if selections:
                total_odds = 1.0
                for s in selections:
                    total_odds *= s["odds"]
                
                tickets.append({
                    "ticket_id": ticket_id + 2,  # Start from 3
                    "ticket_type": "goals_only",
                    "selections": selections,
                    "total_odds": round(total_odds, 2),
                    "expected_return": round(10 * total_odds, 2),
                })
        
        return tickets
    
    def _get_best_result_selection(self, match: Dict) -> Optional[Dict]:
        """Get best result selection for a match (home_win ONLY, NO draws/away_win)."""
        preds = match["predictions"]
        odds = match["odds"]
        
        # Only home_win - EXCLUDE draw and away_win per user request
        options = [
            ("home_win", preds["home_win_conf"], odds["home_win"]),
        ]
        
        # Filter by min odds and ensure odds > 0
        valid = [(m, c, o) for m, c, o in options if o >= self.MIN_ODDS_RESULT and o > 0]
        
        if not valid:
            return None
        
        # Get highest confidence
        best = max(valid, key=lambda x: x[1])
        market, conf, odd = best
        
        return {
            "match_id": match["match_id"],
            "home_team": match["home_team"],
            "away_team": match["away_team"],
            "league": match["league"],
            "date": match["date"],
            "market": market,
            "odds": odd,
            "confidence": round(conf, 3),
            "qualified": False,
            "low_scoring_stats": match.get("low_scoring_stats", {}),
        }
    
    def _get_best_goals_selection(self, match: Dict) -> Optional[Dict]:
        """Get best goals selection for a match."""
        preds = match["predictions"]
        odds = match["odds"]
        qual = match["qualified"]
        
        # Prefer qualified markets
        options = []
        
        if qual["over25"] and preds["over25"] == "YES":
            options.append(("over25", preds["over25_conf"], odds["over25"], True))
        else:
            options.append(("over25", preds["over25_conf"], odds["over25"], False))
        
        if qual["btts"] and preds["btts"] == "YES":
            options.append(("btts", preds["btts_conf"], odds["btts"], True))
        else:
            options.append(("btts", preds["btts_conf"], odds["btts"], False))
        
        # Filter by min odds AND ensure odds > 0 (valid data)
        valid = [(m, c, o, q) for m, c, o, q in options if o >= self.MIN_ODDS_GOALS and o > 0]
        
        if not valid:
            return None
        
        # Prefer qualified, then highest confidence
        valid.sort(key=lambda x: (x[3], x[1]), reverse=True)
        market, conf, odd, is_qual = valid[0]
        
        return {
            "match_id": match["match_id"],
            "home_team": match["home_team"],
            "away_team": match["away_team"],
            "league": match["league"],
            "date": match["date"],
            "market": market,
            "odds": odd,
            "confidence": round(conf, 3),
            "qualified": is_qual,
            "low_scoring_stats": match.get("low_scoring_stats", {}),
        }
