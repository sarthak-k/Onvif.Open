import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAlertSummary } from '../../data/alerts';
import { alertToAnnounce, describeAlert } from './alerts';

type Permission = NotificationPermission | 'unsupported';

const supported = () => typeof window !== 'undefined' && 'Notification' in window;

/** Whether the browser shows desktop notifications, and a way to ask for them. */
export function useNotificationPermission(): [Permission, () => void] {
  const [permission, setPermission] = useState<Permission>(() => (supported() ? Notification.permission : 'unsupported'));
  const request = () => {
    if (supported()) void Notification.requestPermission().then(setPermission);
  };
  return [permission, request];
}

/** While the app is open: a desktop notification for each new alert (when the user allowed them). Clicking it opens Alerts. */
export function useDesktopNotifications() {
  const summary = useAlertSummary();
  const navigate = useNavigate();
  const seen = useRef<string | null | undefined>(undefined);

  useEffect(() => {
    if (!summary.data) return;
    const alert = alertToAnnounce(seen.current, summary.data);
    seen.current = summary.data.latest?.id ?? null;
    if (!alert || !supported() || Notification.permission !== 'granted') return;

    const notification = new Notification(describeAlert(alert), { body: `${alert.ruleName ?? 'Alert'} · ${alert.severity}`, tag: alert.id });
    notification.onclick = () => {
      window.focus();
      navigate('/alerts');
      notification.close();
    };
  }, [summary.data, navigate]);

  return summary.data?.open ?? 0;
}
