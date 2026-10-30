@L3 @group-pre-order
Feature: Organizer removes a participant from the group pre-order

  The organizer can take someone out of the group — a friend who cannot come, or a
  guest who joined by mistake. The removed person's dishes no longer count and are not
  sent to the restaurant. Only the organizer can do it, only for someone who belongs to
  this very group, and only while the group is still open.

  Background:
    Given a restaurant with these dishes on its menu
      | dish         | price |
      | Caesar Salad | 12    |
      | Tom Yum      | 20    |
      | Cheesecake   | 8     |
    And the organizer has booked a table for 6 guests

  Rule: After a removal the organizer sees the remaining guests and the new total

    Background:
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 2        |
      And "Ivan" has joined the group and confirmed a cart with
        | dish    | quantity |
        | Tom Yum | 2        |
      And "Maksym" has joined the group and confirmed a cart with
        | dish       | quantity |
        | Cheesecake | 1        |

    @ui @smoke
    Scenario: The organizer removes one of several guests on the group page
      Given the organizer has opened the group pre-order
      When the organizer removes "Ivan" from the group
      Then the organizer sees these guests
        | guest  | dishes          | subtotal | cart      |
        | Anna   | Caesar Salad x2 | 24       | confirmed |
        | Maksym | Cheesecake x1   | 8        | confirmed |
      And the group total is 32

    @api @smoke
    Scenario: The remaining guests and the total no longer include the removed guest
      When the organizer removes "Ivan" from the group
      Then the organizer sees these guests
        | guest  | dishes          | subtotal | cart      |
        | Anna   | Caesar Salad x2 | 24       | confirmed |
        | Maksym | Cheesecake x1   | 8        | confirmed |
      And the group total is 32
      And the group total equals the sum of everyone's subtotals

    @api
    Scenario: A guest who had confirmed their cart is left out of the order that is sent
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      When the organizer removes "Ivan" from the group
      And the organizer sends the group pre-order to the restaurant
      Then the pre-order sent to the restaurant is
        | dish         | quantity |
        | Caesar Salad | 2        |
        | Cheesecake   | 1        |
        | Tom Yum      | 1        |

    @api
    Scenario: Removing the last guest leaves an empty group
      When the organizer removes "Anna" from the group
      And the organizer removes "Ivan" from the group
      And the organizer removes "Maksym" from the group
      Then nobody is listed in the group
      And the group total is 0

  Rule: Only someone who belongs to this group can be removed

    @api @negative
    Scenario: A guest of another group cannot be removed through this group
      Given another organizer has booked a table for 4 guests
      And "Petro" has joined the group of the other organizer and confirmed a cart with
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When the organizer tries to remove "Petro" from the group
      Then the organizer is told "Guest session does not belong to this group reservation."
      And "Petro" is still in the group of the other organizer

    @api @negative
    Scenario: A guest who does not exist cannot be removed
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
      When the organizer tries to remove a guest who does not exist from the group
      Then the organizer is told that the guest could not be found
      And the organizer sees these guests
        | guest | dishes          | subtotal | cart      |
        | Anna  | Caesar Salad x1 | 12       | confirmed |

    @api @negative
    Scenario: The organizer cannot remove themselves
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      When the organizer tries to remove themselves from the group
      Then the organizer is told "The organizer's own session cannot be removed from the group."

  Rule: Only the organizer can remove participants, and only before the pre-order is sent

    Background:
      Given "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |

    @api @negative
    Scenario Outline: Someone signed in who is not the organizer cannot remove a guest
      When <someone> tries to remove "Anna" from the group
      Then they are told that only the organizer can do this
      And "Anna" is still in the group

      Examples: Signed-in people who are not the organizer
        | someone          |
        | another customer |
        | the waiter       |

    @api @negative
    Scenario: Nobody can remove a guest without signing in
      When an anonymous visitor tries to remove "Anna" from the group
      Then they are asked to sign in first
      And "Anna" is still in the group

    @api @negative
    Scenario: Nobody can be removed once the pre-order has been sent
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And the organizer has sent the group pre-order to the restaurant
      When the organizer tries to remove "Anna" from the group
      Then the organizer is told "Group reservation was submitted."

  Rule: The whole journey, from the guests' carts to the waiter

    @ui @e2e
    Scenario: Guests order, the organizer removes one of them and sends, the waiter sees the result
      Given the organizer has pre-ordered for themselves
        | dish    | quantity |
        | Tom Yum | 1        |
      And "Anna" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 2        |
      And "Ivan" has joined the group and confirmed a cart with
        | dish    | quantity |
        | Tom Yum | 2        |
      And "Maksym" has joined the group and confirmed a cart with
        | dish         | quantity |
        | Caesar Salad | 1        |
        | Cheesecake   | 1        |
      When the organizer opens the group pre-order
      Then the organizer sees these guests
        | guest  | dishes                          | subtotal | cart      |
        | Anna   | Caesar Salad x2                 | 24       | confirmed |
        | Ivan   | Tom Yum x2                      | 40       | confirmed |
        | Maksym | Caesar Salad x1, Cheesecake x1  | 20       | confirmed |
      And the group total is 104
      When the organizer removes "Ivan" from the group
      Then the group total is 64
      When the organizer sends the group pre-order to the restaurant
      Then the organizer sees that the pre-order was sent to the restaurant
      When the waiter opens the pre-order of the reservation
      Then the waiter sees these dishes
        | dish         | quantity |
        | Caesar Salad | 3        |
        | Cheesecake   | 1        |
        | Tom Yum      | 1        |
