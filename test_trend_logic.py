#!/usr/bin/env python3
"""
Test the trend tracking logic locally
"""

# Simulate the trend tracking logic from server.py
current_trend = {
    'dir': None,
    'start_time': None,
    'start_bar': None,
}

trend_segments = []

def process_bar(bar_index, trend_side, local_time):
    """Process a bar and track trend changes"""
    global current_trend, trend_segments
    
    if trend_side and bar_index is not None:
        # Check if trend has changed
        if current_trend['dir'] is None:
            # First trend initialization
            current_trend['dir'] = trend_side
            current_trend['start_bar'] = bar_index
            current_trend['start_time'] = local_time
            print(f"[TREND] Initial trend: {trend_side} starting at bar {bar_index}")
        elif current_trend['dir'] != trend_side:
            # Trend has changed - record the previous trend segment
            trend_segment = {
                'side': current_trend['dir'],
                'startBarIndex': current_trend['start_bar'],
                'startTime': current_trend['start_time'],
                'endBarIndex': bar_index - 1,
                'endTime': local_time,
                'duration': (bar_index - 1) - current_trend['start_bar'] + 1
            }
            trend_segments.append(trend_segment)
            print(f"[TREND] Segment complete: {trend_segment['side']} from bar {trend_segment['startBarIndex']} to {trend_segment['endBarIndex']} ({trend_segment['duration']} bars)")
            
            # Start new trend
            current_trend['dir'] = trend_side
            current_trend['start_bar'] = bar_index
            current_trend['start_time'] = local_time
            print(f"[TREND] New trend: {trend_side} starting at bar {bar_index}")

# Test with sample data
print("=== TREND TRACKING TEST ===\n")

# Simulate: 5 bars BULL, then 3 bars BEAR, then 4 bars BULL
test_data = [
    (100, 'BULL', '10:00:00'),
    (101, 'BULL', '10:01:00'),
    (102, 'BULL', '10:02:00'),
    (103, 'BULL', '10:03:00'),
    (104, 'BULL', '10:04:00'),
    (105, 'BEAR', '10:05:00'),  # Trend flip detected
    (106, 'BEAR', '10:06:00'),
    (107, 'BEAR', '10:07:00'),
    (108, 'BULL', '10:08:00'),  # Trend flip detected
    (109, 'BULL', '10:09:00'),
    (110, 'BULL', '10:10:00'),
    (111, 'BULL', '10:11:00'),
]

for bar_index, trend_side, local_time in test_data:
    process_bar(bar_index, trend_side, local_time)

print("\n=== TREND SEGMENTS ===")
for seg in trend_segments:
    print(f"  {seg['side']}: bars {seg['startBarIndex']}-{seg['endBarIndex']} ({seg['duration']} bars)")

print(f"\nCurrent trend: {current_trend['dir']} starting at bar {current_trend['start_bar']}")
print(f"Total segments recorded: {len(trend_segments)}")

# Test the lookup logic (like dashboard does)
print("\n=== TREND LOOKUP TEST ===")
test_bar_index = 106
test_bar_trend_side = 'BEAR'
trendStartBar = None
for seg in trend_segments:
    if (seg['side'] == test_bar_trend_side and 
        seg['startBarIndex'] <= test_bar_index and 
        (not seg.get('endBarIndex') or seg['endBarIndex'] >= test_bar_index)):
        trendStartBar = seg['startBarIndex']
        break

print(f"For bar {test_bar_index} with trend {test_bar_trend_side}:")
print(f"  Trend start bar: {trendStartBar}")
print(f"  Display: {test_bar_trend_side}{f' (from {trendStartBar})' if trendStartBar else ''}")
