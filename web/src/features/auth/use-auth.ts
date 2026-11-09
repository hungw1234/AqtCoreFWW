import { useContext } from 'react';
import { AuthContext } from './auth-provider';

// Hook to consume AuthContext — must be used inside AuthProvider
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used within AuthProvider');
  }
  return ctx;
}
