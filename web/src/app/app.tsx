import '@ant-design/v5-patch-for-react-19';
import { RouterProvider } from '@tanstack/react-router';
import { router } from './router';
import { Providers } from './providers';

// App root — composes providers and router
export function App() {
  return (
    <Providers>
      <RouterProvider router={router} />
    </Providers>
  );
}
