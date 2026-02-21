-- IsCurrentSeason Fix Script
-- Current season is 2025 (Feb 2026, month < 7 means season = 2025)
-- Football season = Year of July-Dec, or Year-1 of Jan-Jun

-- Step 1: Show current state
SELECT 'BEFORE FIX' as Status;
SELECT 
    CASE 
        WHEN strftime('%m', Date) >= '07' THEN strftime('%Y', Date) 
        ELSE CAST(strftime('%Y', Date) - 1 AS TEXT)
    END as Season,
    IsCurrentSeason,
    COUNT(*) as Count
FROM Fixtures 
GROUP BY Season, IsCurrentSeason
ORDER BY Season, IsCurrentSeason;

-- Step 2: Fix IsCurrentSeason flag
-- Set to 1 ONLY for season 2025, otherwise 0
UPDATE Fixtures 
SET IsCurrentSeason = CASE 
    WHEN (CASE 
        WHEN strftime('%m', Date) >= '07' THEN strftime('%Y', Date) 
        ELSE CAST(strftime('%Y', Date) - 1 AS TEXT)
    END) = '2025' THEN 1
    ELSE 0
END;

-- Step 3: Show state after fix
SELECT 'AFTER FIX' as Status;
SELECT 
    CASE 
        WHEN strftime('%m', Date) >= '07' THEN strftime('%Y', Date) 
        ELSE CAST(strftime('%Y', Date) - 1 AS TEXT)
    END as Season,
    IsCurrentSeason,
    COUNT(*) as Count
FROM Fixtures 
GROUP BY Season, IsCurrentSeason
ORDER BY Season, IsCurrentSeason;
