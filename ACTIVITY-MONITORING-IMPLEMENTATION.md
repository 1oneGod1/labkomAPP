# 📊 Activity Monitoring System - Implementation Guide

## ✅ Status Implementasi

### Completed
- [x] Database schema created (`activity_logs` & `activity_categories`)
- [x] ActivityMonitor class implemented
- [x] npm package `active-win` installed
- [x] ActivityMonitor imported to main.js

### In Progress
- [ ] Integrate ActivityMonitor with login/logout flow
- [ ] Send activity data via Socket.IO to server
- [ ] Create server API endpoints
- [ ] Build admin dashboard UI

## 🔧 Integration Steps

### Step 1: Initialize ActivityMonitor in main.js

Add after the activityMonitor variable declaration:

```javascript
// â"€â"€ Activity Monitor Functions â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
function initializeActivityMonitor() {
  if (activityMonitor) return;
  
  activityMonitor = new ActivityMonitor();
  
  // Set callback untuk mengirim activity ke server
  activityMonitor.onActivity((activity) => {
    sendActivityToServer(activity);
  });
  
  log.info('[ACTIVITY] ActivityMonitor initialized');
}

function sendActivityToServer(activity) {
  // Send via Socket.IO jika connected
  if (realtimeSocket?.connected) {
    realtimeSocket.emit('client:activity', activity);
    return;
  }
  
  // Fallback: send via HTTP
  const cfg = loadServerConfig();
  if (!cfg.serverUrl) return;
  
  try {
    const parsed = new URL(`${cfg.serverUrl}/api/activities`);
    const body = JSON.stringify(activity);
    
    const req = http.request({
      hostname: parsed.hostname,
      port: parseInt(parsed.port) || 3001,
      path: '/api/activities',
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body)
      },
    }, (res) => {
      res.resume(); // Consume response
    });
    
    req.on('error', () => {}); // Silent error
    req.setTimeout(3000, () => req.destroy());
    req.write(body);
    req.end();
  } catch (_) {}
}

function startActivityMonitoring(studentData, sessionId) {
  if (!activityMonitor) initializeActivityMonitor();
  
  // Set student info
  activityMonitor.setStudentInfo({
    pc_name: screenShareState.pcName,
    student_id: studentData?.id || null,
    student_name: studentData?.nama_lengkap || null,
    session_id: sessionId || null,
  });
  
  // Start monitoring
  activityMonitor.start();
  log.info('[ACTIVITY] Monitoring started for:', studentData?.nama_lengkap);
}

function stopActivityMonitoring() {
  if (!activityMonitor) return;
  
  activityMonitor.stop();
  log.info('[ACTIVITY] Monitoring stopped');
}
```

### Step 2: Integrate with Login/Logout

Modify `login-success` handler:

```javascript
ipcMain.on('login-success', (_event, studentData) => {
  if (!mainWindow) return;

  applyWindowLayout('checklist');
  mainWindow.webContents.send('kiosk-off', studentData);

  // Mulai screen share
  const cfg = loadServerConfig();
  if (cfg.serverUrl) {
    startScreenShare(cfg.serverUrl, studentData?.nama_lengkap);
    
    // â†" START ACTIVITY MONITORING
    startActivityMonitoring(studentData, studentData?.session_id);
  }
});
```

Modify `do-logout` handler:

```javascript
ipcMain.on('do-logout', () => {
  if (!mainWindow) return;

  stopScreenShare();
  
  // â†" STOP ACTIVITY MONITORING
  stopActivityMonitoring();

  applyWindowLayout('login');
  scheduleFocusRecovery(50);
  mainWindow.webContents.send('return-to-login');
});
```

### Step 3: Cleanup on App Quit

Add to `app.on('will-quit')`:

```javascript
app.on('will-quit', () => {
  stopDiscoveryListener();
  stopPresenceHeartbeat();
  disconnectRealtime();
  
  // â†" CLEANUP ACTIVITY MONITOR
  stopActivityMonitoring();
  
  if (cmdPollTimer) { clearInterval(cmdPollTimer); cmdPollTimer = null; }
  globalShortcut.unregisterAll();
});
```

### Step 4: Socket.IO Event Handler (Server Side)

Add to `server/src/realtimeHub.js`:

```javascript
// Handle client activity data
socket.on('client:activity', (activity) => {
  const { pc_name, student_id, session_id } = activity;
  
  // Broadcast to admin dashboard
  io.to('admin').emit('activity:new', activity);
  
  // Save to database
  saveActivityToDatabase(activity).catch(err => {
    console.error('[ACTIVITY] Failed to save:', err.message);
  });
});

async function saveActivityToDatabase(activity) {
  const query = `
    INSERT INTO activity_logs (
      pc_name, student_id, student_name, session_id,
      activity_type, window_title, process_name, process_path,
      browser_name, url, url_domain, page_title,
      running_apps, duration_seconds, activity_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `;
  
  const values = [
    activity.pc_name,
    activity.student_id,
    activity.student_name,
    activity.session_id,
    activity.activity_type,
    activity.window_title,
    activity.process_name,
    activity.process_path,
    activity.browser_name,
    activity.url,
    activity.url_domain,
    activity.page_title,
    activity.running_apps,
    activity.duration_seconds,
    activity.activity_at || new Date(),
  ];
  
  await db.query(query, values);
}
```

## 📡 Server API Endpoints

Create `server/src/controllers/activitiesController.js`:

```javascript
const db = require('../config/database');

// POST /api/activities - Receive activity data from client
exports.createActivity = async (req, res) => {
  try {
    const activity = req.body;
    
    const query = `
      INSERT INTO activity_logs (
        pc_name, student_id, student_name, session_id,
        activity_type, window_title, process_name, process_path,
        browser_name, url, url_domain, page_title,
        running_apps, duration_seconds, activity_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `;
    
    const values = [
      activity.pc_name,
      activity.student_id || null,
      activity.student_name || null,
      activity.session_id || null,
      activity.activity_type,
      activity.window_title || null,
      activity.process_name || null,
      activity.process_path || null,
      activity.browser_name || null,
      activity.url || null,
      activity.url_domain || null,
      activity.page_title || null,
      activity.running_apps || null,
      activity.duration_seconds || 0,
      activity.activity_at || new Date(),
    ];
    
    await db.query(query, values);
    
    res.json({ success: true, message: 'Activity logged' });
  } catch (error) {
    console.error('[API] Error logging activity:', error);
    res.status(500).json({ error: 'Failed to log activity' });
  }
};

// GET /api/activities - Get activity logs (admin only)
exports.getActivities = async (req, res) => {
  try {
    const { pc_name, student_id, session_id, limit = 100 } = req.query;
    
    let query = 'SELECT * FROM activity_logs WHERE 1=1';
    const params = [];
    
    if (pc_name) {
      query += ' AND pc_name = ?';
      params.push(pc_name);
    }
    
    if (student_id) {
      query += ' AND student_id = ?';
      params.push(student_id);
    }
    
    if (session_id) {
      query += ' AND session_id = ?';
      params.push(session_id);
    }
    
    query += ' ORDER BY activity_at DESC LIMIT ?';
    params.push(parseInt(limit));
    
    const [rows] = await db.query(query, params);
    
    res.json({ success: true, activities: rows });
  } catch (error) {
    console.error('[API] Error fetching activities:', error);
    res.status(500).json({ error: 'Failed to fetch activities' });
  }
};

// GET /api/activities/summary - Get activity summary per student
exports.getActivitySummary = async (req, res) => {
  try {
    const query = `
      SELECT * FROM activity_summary
      ORDER BY last_activity DESC
    `;
    
    const [rows] = await db.query(query);
    
    res.json({ success: true, summary: rows });
  } catch (error) {
    console.error('[API] Error fetching summary:', error);
    res.status(500).json({ error: 'Failed to fetch summary' });
  }
};
```

Add routes in `server/src/index.js`:

```javascript
const activitiesController = require('./controllers/activitiesController');

// Activity monitoring routes
app.post('/api/activities', activitiesController.createActivity);
app.get('/api/activities', activitiesController.getActivities);
app.get('/api/activities/summary', activitiesController.getActivitySummary);
```

## 🖥️ Admin Dashboard UI Component

Create `admin/src/ActivityMonitor.jsx`:

```jsx
import React, { useState, useEffect } from 'react';

export default function ActivityMonitor({ socket }) {
  const [activities, setActivities] = useState([]);
  const [summary, setSummary] = useState([]);

  useEffect(() => {
    if (!socket) return;

    // Listen for new activities
    socket.on('activity:new', (activity) => {
      setActivities(prev => [activity, ...prev.slice(0, 99)]);
    });

    // Fetch initial summary
    fetchSummary();

    return () => {
      socket.off('activity:new');
    };
  }, [socket]);

  const fetchSummary = async () => {
    try {
      const res = await fetch('/api/activities/summary');
      const data = await res.json();
      if (data.success) {
        setSummary(data.summary);
      }
    } catch (err) {
      console.error('Failed to fetch summary:', err);
    }
  };

  const getActivityIcon = (type) => {
    switch (type) {
      case 'browser_url': return '🌐';
      case 'window_change': return '💻';
      case 'app_list': return '📋';
      default: return '📌';
    }
  };

  return (
    <div className="activity-monitor">
      <h2 className="text-xl font-bold mb-4">🔴 Live Activity Feed</h2>
      
      <div className="activity-list space-y-2 max-h-96 overflow-y-auto">
        {activities.map((activity, idx) => (
          <div key={idx} className="activity-item p-3 bg-gray-50 rounded-lg">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="font-semibold text-sm">
                  {getActivityIcon(activity.activity_type)} {activity.pc_name} | {activity.student_name}
                </div>
                <div className="text-sm text-gray-600 mt-1">
                  {activity.activity_type === 'browser_url' && (
                    <div>
                      <span className="font-medium">🌐 Browsing:</span> {activity.url_domain}
                      {activity.page_title && <div className="text-xs text-gray-500">{activity.page_title}</div>}
                    </div>
                  )}
                  {activity.activity_type === 'window_change' && (
                    <div>
                      <span className="font-medium">💻 Using:</span> {activity.process_name} - {activity.window_title}
                    </div>
                  )}
                  {activity.activity_type === 'app_list' && (
                    <div>
                      <span className="font-medium">📋 Applications:</span> {JSON.parse(activity.running_apps || '[]').length} running
                    </div>
                  )}
                </div>
              </div>
              <div className="text-xs text-gray-400">
                {new Date(activity.activity_at).toLocaleTimeString()}
              </div>
            </div>
          </div>
        ))}
      </div>

      <div className="mt-6">
        <h3 className="text-lg font-semibold mb-3">📊 Activity Summary</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {summary.map((item, idx) => (
            <div key={idx} className="p-4 bg-white border rounded-lg shadow-sm">
              <div className="font-semibold">{item.student_name}</div>
              <div className="text-sm text-gray-600">{item.pc_name}</div>
              <div className="mt-2 text-xs space-y-1">
                <div>✅ Productive: {item.productive_count}</div>
                <div>⚠️ Unproductive: {item.unproductive_count}</div>
                <div>📊 Total: {item.activity_count}</div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
```

## 🚀 Quick Implementation Checklist

1. **Add functions to main.js** (copy from Step 1, 2, 3 above)
2. **Create server controller** (`activitiesController.js`)
3. **Add routes** to server index.js
4. **Update realtimeHub** with activity event handler
5. **Create React component** for admin dashboard
6. **Run database migration** (`activity-logs-schema.sql`)
7. **Test the flow**:
   - Login as student
   - Open various apps
   - Browse websites
   - Check admin dashboard for live feed

## 🧪 Testing Commands

```bash
# Run database migration
mysql -u root -p labkom < database/activity-logs-schema.sql

# Test activity endpoint
curl -X POST http://localhost:3001/api/activities \
  -H "Content-Type: application/json" \
  -d '{
    "pc_name": "PC-01",
    "activity_type": "browser_url",
    "url": "https://github.com",
    "activity_at": "2026-04-02T10:00:00Z"
  }'

# Fetch activities
curl http://localhost:3001/api/activities?limit=10
```

## 📝 Configuration Options

Adjust monitoring behavior in ActivityMonitor config:

```javascript
activityMonitor.updateConfig({
  windowPollMs: 5000,      // Check every 5 seconds (less aggressive)
  appListPollMs: 60000,    // Get app list every minute
  enableUrlTracking: true, // Enable/disable URL tracking
  enableAppList: false,    // Disable app list if not needed
});
```

## 🔒 Privacy Controls

Add privacy settings per lab or per session:

```javascript
// Disable activity monitoring untuk sesi tertentu
if (sessionConfig.privateMode) {
  stopActivityMonitoring();
}

// Filter sensitive data
function sanitizeActivity(activity) {
  if (activity.window_title?.includes('password')) {
    activity.window_title = '[password entry]';
  }
  return activity;
}
```

## 📚 Next Steps

1. Implement the integration code above
2. Test with real client login/logout
3. Verify data appears in database
4. Check admin dashboard shows live feed
5. Add filtering/search in admin UI
6. Implement privacy controls
7. Add data retention policy execution

---

**Status**: Implementation guide complete
**Last Updated**: 2026-04-02
**Ready for**: Integration & Testing
