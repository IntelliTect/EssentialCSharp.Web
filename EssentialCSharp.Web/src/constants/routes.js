/**
 * Centralized route constants for the frontend.
 * Mirrors the backend RouteConstants to ensure consistency across frontend and backend.
 * Update these whenever routes are added, removed, or changed.
 */

export const ROUTES = {
    HOME: '/home',
    ABOUT: '/about',
    GUIDELINES: '/guidelines',
    ANNOUNCEMENTS: '/announcements',
    TERMS_OF_SERVICE: '/termsofservice'
};

/**
 * Array of non-content route paths.
 * Use with Array.includes() to determine if a path is a static page vs. a content page.
 */
export const NON_CONTENT_ROUTES = [
    ROUTES.HOME,
    ROUTES.ABOUT,
    ROUTES.GUIDELINES,
    ROUTES.ANNOUNCEMENTS,
    ROUTES.TERMS_OF_SERVICE
];

/**
 * Navigation link definitions for the sidebar.
 * Each link includes href, label, icon class, and active path matching patterns.
 * Note: ROUTES.TERMS_OF_SERVICE is intentionally excluded from this list —
 * it is a legal page linked in the footer, not a primary navigation destination.
 */
export const NAVIGATION_LINKS = [
    {
        href: ROUTES.HOME,
        label: 'Home',
        iconClass: 'fas fa-home me-2',
        activePaths: ['/', ROUTES.HOME],
        key: 'home'
    },
    {
        href: ROUTES.ABOUT,
        label: 'About',
        iconClass: 'fas fa-book me-2',
        activePaths: [ROUTES.ABOUT],
        key: 'about'
    },
    {
        href: ROUTES.GUIDELINES,
        label: 'Guidelines',
        iconClass: 'fas fa-code me-2',
        activePaths: [ROUTES.GUIDELINES],
        key: 'guidelines'
    },
    {
        href: ROUTES.ANNOUNCEMENTS,
        label: 'Announcements',
        iconClass: 'fas fa-bullhorn me-2',
        activePaths: [ROUTES.ANNOUNCEMENTS],
        key: 'announcements'
    }
];

/**
 * Determines if the given path is a content page (from sitemap)
 * versus a static page (non-content).
 * @param {string} path - The path to check
 * @returns {boolean} - True if path is a content page, false if static page
 */
export function isContentPagePath(path) {
    const normalizedPath = path.toLowerCase();
    return !NON_CONTENT_ROUTES.some(route => 
        normalizedPath === route.toLowerCase() || 
        normalizedPath.startsWith(route.toLowerCase() + '/')
    );
}
